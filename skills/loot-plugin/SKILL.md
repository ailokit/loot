---
name: loot-plugin
description: Create, modify, debug, or package plugins and UI tools for the Loot desktop application. Use when working with Loot plugin manifests, JavaScript actions, plugin installation, or Loot's Avalonia json-render UI.
---

# Loot plugin development

Use this skill when implementing a Loot extension package or one of its UI tools. First inspect the repository's plugin design doc, current plugin host/runtime, and `examples/plugins/hello`; implementation details can change, so treat the checked-out code as the source of truth.

## Workflow

1. Read `docs/plugin-system-design.md` for the package and host model, then inspect the relevant code under `src/Loot/Plugins`, `src/Loot.JsonRender.Avalonia`, `src/native/quickjs`, and the example plugin. Follow current behavior if it differs from the design document.
2. Keep each plugin self-contained under the Loot data directory's `plugins/<manifest-id>/` folder. Use `plugin.json`, a separate `ui.json` per tool, and a separate JavaScript entry file. The installed directory name must exactly match the manifest `id`; all tool `ui` and `entry` paths must resolve inside that package.
3. Use the existing example as a working template, updating its manifest, JSON UI, and JS behavior together when extending the example. For new UI work, read [references/json-render-avalonia.md](references/json-render-avalonia.md) before writing the spec.
4. Keep plugin logic in the supported JavaScript runtime API. Do not assume filesystem, network, native module, ES module import, or arbitrary Avalonia/XAML access exists; inspect the current host API before using any capability.
5. Handle plugin failures at plugin boundaries: report contextual errors through the existing Loot logger/error sink, keep malformed manifests/specs/scripts and action failures from escaping into the desktop event loop, and preserve the main application's ability to continue. Do not hide failures with silent catch blocks.
6. When changing a feature, update the narrowest relevant layer and its documentation/example. If adding renderer support, keep component factories in their component family and register them through `JsonRenderComponentRegistry`; plugin JSON can select host-registered components but cannot provide CLR component code.
7. Verify the requested path end to end where practical: validate manifest paths and IDs, open the tool, exercise its actions/bindings, and inspect logs for expected failures. Follow repository instructions about whether tests/builds should be run.

## References

- For UI spec syntax, supported expressions, bindings, watchers, components, and extension points, read [references/json-render-avalonia.md](references/json-render-avalonia.md).
- For package shape and manifest fields, inspect `docs/plugin-system-design.md`, `src/Loot/Plugins/PluginManifest.cs`, `src/Loot/Plugins/PluginCatalog.cs`, and `examples/plugins/hello/plugin.json`.
- For JavaScript host API and runtime behavior, inspect `src/native/quickjs/loot_qjs.c`, `src/Loot/Plugins/QuickJsPluginScriptRuntime.cs`, and `examples/plugins/hello/tools/hello/main.js`.
