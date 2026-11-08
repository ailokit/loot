#include "quickjs.h"
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#if defined(_WIN32)
#  define LOOT_EXPORT __declspec(dllexport)
#else
#  define LOOT_EXPORT __attribute__((visibility("default")))
#endif

typedef void (*loot_message_callback)(void *runtime, const char *name, const char *payload);

typedef struct loot_runtime {
    JSRuntime *runtime;
    JSContext *context;
    loot_message_callback callback;
    clock_t deadline;
    int interrupted;
} loot_runtime;

static char *copy_string(const char *value) {
    size_t length = strlen(value);
    char *copy = (char *)malloc(length + 1);
    if (copy) memcpy(copy, value, length + 1);
    return copy;
}

static char *value_to_utf8(JSContext *context, JSValueConst value) {
    JSValue json = JS_JSONStringify(context, value, JS_UNDEFINED, JS_UNDEFINED);
    if (JS_IsException(json)) return NULL;
    const char *text = JS_ToCString(context, json);
    char *copy = text ? copy_string(text) : NULL;
    if (text) JS_FreeCString(context, text);
    JS_FreeValue(context, json);
    return copy;
}

static int send_message(JSContext *context, const char *name, JSValueConst value) {
    loot_runtime *instance = (loot_runtime *)JS_GetContextOpaque(context);
    if (!instance || !instance->callback) return 0;
    char *payload = value_to_utf8(context, value);
    if (!payload) return 1;
    if (payload) {
        if (strlen(payload) > 1024 * 1024) {
            free(payload);
            JS_ThrowRangeError(context, "Host message exceeds 1 MiB");
            return 1;
        }
        instance->callback(instance, name, payload);
        free(payload);
    }
    return 0;
}

static JSValue js_host_message(JSContext *context, JSValueConst this_value, int argc, JSValueConst *argv, int magic, JSValueConst *function_data) {
    (void)this_value; (void)function_data;
    if (argc < 1) return JS_ThrowTypeError(context, "payload is required");
    if (send_message(context, magic == 0 ? "setState" : "notify", argv[0])) return JS_EXCEPTION;
    return JS_UNDEFINED;
}

static int interrupt_handler(JSContext *context, void *opaque) {
    (void)context;
    loot_runtime *instance = (loot_runtime *)opaque;
    if (instance && instance->deadline != 0 && clock() >= instance->deadline) {
        instance->interrupted = 1;
        return 1;
    }
    return 0;
}

static void set_error(JSContext *context, char **error, const char *fallback) {
    JSValue exception = JS_GetException(context);
    const char *message = JS_ToCString(context, exception);
    *error = copy_string(message ? message : fallback);
    if (message) JS_FreeCString(context, message);
    JS_FreeValue(context, exception);
}

static int begin_call(loot_runtime *instance, char **error) {
    instance->interrupted = 0;
    instance->deadline = clock() + 2 * CLOCKS_PER_SEC;
    /* Managed calls may resume on a different thread-pool thread between actions.
       QuickJS uses the current thread's stack pointer for overflow checks. */
    JS_UpdateStackTop(instance->runtime);
    (void)error;
    return 0;
}

static void end_call(loot_runtime *instance) { instance->deadline = 0; }

static int execute_jobs(loot_runtime *instance, char **error) {
    JSContext *job_context = NULL;
    int result;
    for (int i = 0; i < 1000; i++) {
        result = JS_ExecutePendingJob(instance->runtime, &job_context);
        if (result == 0) return 0;
        if (result < 0) {
            set_error(job_context ? job_context : instance->context, error, "pending JavaScript job failed");
            return 1;
        }
    }
    *error = copy_string("JavaScript job queue limit exceeded");
    return 1;
}

LOOT_EXPORT void *loot_qjs_create(size_t max_heap_bytes, size_t max_stack_bytes, loot_message_callback callback) {
    loot_runtime *instance = (loot_runtime *)calloc(1, sizeof(loot_runtime));
    if (!instance) return NULL;
    instance->callback = callback;
    instance->runtime = JS_NewRuntime();
    if (!instance->runtime) { free(instance); return NULL; }
    JS_SetMemoryLimit(instance->runtime, max_heap_bytes);
    JS_SetMaxStackSize(instance->runtime, max_stack_bytes);
    JS_SetRuntimeOpaque(instance->runtime, instance);
    JS_SetInterruptHandler(instance->runtime, interrupt_handler, instance);
    instance->context = JS_NewContext(instance->runtime);
    if (!instance->context) { JS_FreeRuntime(instance->runtime); free(instance); return NULL; }
    JS_SetContextOpaque(instance->context, instance);

    JSValue loot = JS_NewObject(instance->context);
    JSValue ui = JS_NewObject(instance->context);
    JSValue set_state = JS_NewCFunctionData(instance->context, js_host_message, 1, 0, 0, NULL);
    JSValue notify = JS_NewCFunctionData(instance->context, js_host_message, 1, 1, 0, NULL);
    JS_SetPropertyStr(instance->context, ui, "setState", set_state);
    JS_SetPropertyStr(instance->context, ui, "notify", notify);
    JS_SetPropertyStr(instance->context, loot, "ui", ui);
    JSValue global = JS_GetGlobalObject(instance->context);
    JS_SetPropertyStr(instance->context, global, "loot", loot);
    JS_FreeValue(instance->context, global);
    return instance;
}

LOOT_EXPORT int loot_qjs_eval(void *handle, const char *source, const char *filename, char **error) {
    loot_runtime *instance = (loot_runtime *)handle;
    *error = NULL;
    begin_call(instance, error);
    JSValue result = JS_Eval(instance->context, source, strlen(source), filename, JS_EVAL_TYPE_GLOBAL);
    end_call(instance);
    if (JS_IsException(result)) { set_error(instance->context, error, "script evaluation failed"); return 1; }
    JS_FreeValue(instance->context, result);
    begin_call(instance, error);
    int jobs_result = execute_jobs(instance, error);
    end_call(instance);
    return jobs_result;
}

LOOT_EXPORT int loot_qjs_call_open(void *handle, char **error) {
    loot_runtime *instance = (loot_runtime *)handle;
    *error = NULL;
    JSValue global = JS_GetGlobalObject(instance->context);
    JSValue plugin = JS_GetPropertyStr(instance->context, global, "plugin");
    JSValue function = JS_GetPropertyStr(instance->context, plugin, "onOpen");
    if (!JS_IsFunction(instance->context, function)) {
        JS_FreeValue(instance->context, function); JS_FreeValue(instance->context, plugin); JS_FreeValue(instance->context, global);
        return 0;
    }
    begin_call(instance, error);
    JSValue result = JS_Call(instance->context, function, plugin, 0, NULL);
    int jobs_result = JS_IsException(result) ? 1 : execute_jobs(instance, error);
    if (JS_IsException(result)) set_error(instance->context, error, "onOpen handler failed");
    JS_FreeValue(instance->context, result);
    end_call(instance);
    JS_FreeValue(instance->context, function); JS_FreeValue(instance->context, plugin); JS_FreeValue(instance->context, global);
    return jobs_result;
}

LOOT_EXPORT int loot_qjs_dispatch_action(void *handle, const char *action, const char *payload, char **error) {
    loot_runtime *instance = (loot_runtime *)handle;
    *error = NULL;
    JSValue global = JS_GetGlobalObject(instance->context);
    JSValue plugin = JS_GetPropertyStr(instance->context, global, "plugin");
    JSValue function = JS_GetPropertyStr(instance->context, plugin, "onAction");
    if (!JS_IsFunction(instance->context, function)) {
        JS_FreeValue(instance->context, function); JS_FreeValue(instance->context, plugin); JS_FreeValue(instance->context, global);
        return 0;
    }
    JSValue argument = JS_NewString(instance->context, action);
    JSValue payload_value = JS_ParseJSON(instance->context, payload, strlen(payload), "action.json");
    JSValue arguments[] = { argument, payload_value };
    begin_call(instance, error);
    JSValue result = JS_Call(instance->context, function, plugin, 2, arguments);
    end_call(instance);
    JS_FreeValue(instance->context, argument); JS_FreeValue(instance->context, payload_value);
    JS_FreeValue(instance->context, function); JS_FreeValue(instance->context, plugin); JS_FreeValue(instance->context, global);
    if (JS_IsException(result)) { set_error(instance->context, error, "action handler failed"); return 1; }
    JS_FreeValue(instance->context, result);
    begin_call(instance, error);
    int jobs_result = execute_jobs(instance, error);
    end_call(instance);
    return jobs_result;
}

LOOT_EXPORT void loot_qjs_free_string(char *value) { free(value); }

LOOT_EXPORT void loot_qjs_destroy(void *handle) {
    loot_runtime *instance = (loot_runtime *)handle;
    if (!instance) return;
    JS_FreeContext(instance->context);
    JS_FreeRuntime(instance->runtime);
    free(instance);
}
