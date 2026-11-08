# Loot json-render UI reference

Use this reference when authoring a plugin tool's `ui.json`, diagnosing a render error, or extending Loot's renderer. It is intentionally limited to the implementation in `Loot.JsonRender.Avalonia`; the upstream json-render specification and framework skills describe more features than Loot currently supports.

## How to read upstream json-render material

Use the upstream [specs](https://json-render.dev/docs/specs), [data binding guide](https://json-render.dev/docs/data-binding), and [React skill](https://github.com/vercel-labs/json-render/blob/main/skills/react/SKILL.md) for concepts and design inspiration. Treat Loot's renderer and `examples/plugins/hello/tools/hello/ui.json` as authoritative for executable syntax. The upstream React skill includes framework-specific features that do not automatically apply to Loot.

Do not claim or generate these as supported without first implementing them in Loot: React components/providers or hooks, React `on` event declarations, arbitrary JSON Schema validation, streaming, dynamic component catalogs or JSX, unimplemented state mutation actions such as push/remove, or spec forms beyond what the current renderer accepts. Loot buttons use `props.action`; input bindings update the state store directly.

## Spec shape

Loot accepts a flat element map with a root ID. Child relationships refer to element IDs, not nested element objects:

```json
{
  "root": "page",
  "state": { "name": "朋友", "showDetails": true, "items": ["Alpha", "Beta"] },
  "elements": {
    "page": { "type": "ScrollView", "children": ["content"] },
    "content": { "type": "Column", "props": { "spacing": 12 }, "children": ["heading", "name", "submit", "list"] },
    "heading": { "type": "Text", "props": { "variant": "title", "content": "欢迎" } },
    "name": { "type": "TextField", "props": { "label": "名字", "value": { "$bindState": "/name" } } },
    "submit": { "type": "Button", "props": { "label": "生成", "action": "greet", "params": { "name": { "$state": "/name" } } } },
    "list": { "type": "Column", "repeat": { "statePath": "/items" }, "children": ["item"] },
    "item": { "type": "Text", "props": { "content": { "$item": "" } } }
  }
}
```

Required top-level fields are string `root` and object `elements`. An element has a registered `type` and may specify object `props`, ID array `children`, named `slots` whose values are ID arrays, `visible`, `repeat`, and `watch`. Optional top-level `state` initializes tool state. IDs must resolve, and child cycles are rejected. The renderer currently limits specs to 1 MiB, 500 elements, depth 32, 500 repeated items, and 5,000 rendered controls; check the constants before relying on these values.

Slots are interpreted by individual host components, not by a universal slot renderer. For example `Card` recognizes `header` and `footer`; other layout containers append provided slots. Check that component's factory before using a named slot.

## Expressions and state

The current renderer resolves the following expression objects inside props and watcher parameters:

- `{ "$state": "/path" }`: read a value using a JSON Pointer into state.
- `{ "$bindState": "/path" }`: two-way binding for supported input props (`value`, `checked`, or `pressed` depending on component).
- `{ "$bindItem": "field" }`: two-way bind a field of the current repeated item; an empty string binds the item itself.
- `{ "$item": "field" }` or `{ "$item": "" }`: read the current repeated item or one of its fields.
- `{ "$index": true }`: read the zero-based repeated-item index.
- `{ "$template": "Hello ${/name}" }`: interpolate JSON Pointer state references.
- `{ "$cond": <condition>, "$then": <value>, "$else": <value> }`: choose a value from a visibility condition.
- `{ "$computed": "name", "args": { ... } }`: call a built-in or host-registered computation. Built-ins are `concat`, `sum`, `uppercase`, `lowercase`, and `join`; custom functions exist only where a trusted host caller registers them.

State paths are JSON Pointers such as `/form/name`; escape `~` as `~0` and `/` as `~1`. A control must use the component's supported binding property. Plain `$state` is read-only. UI bindings update state, and JavaScript can update top-level state through `loot.ui.setState(patch)`. Use existing sample conventions for nested state; inspect the host's merge behavior before assuming nested patches merge recursively.

Visibility accepts booleans/truthy values, `{ "$state": "/path" }`, a state comparison (`eq`, `neq`, `gt`, `gte`, `lt`, `lte`), `not: true`, arrays (all conditions), and `{ "$or": [...] }`. Comparisons resolve expected expression values. Loot rebuilds the control tree after state changes, so visibility and other resolved props are evaluated again. As with any full rebuild, avoid unnecessary state updates while a user is actively editing a field.

`repeat` has the form `{ "statePath": "/array/path" }`; nested repeats can use `{ "statePath": { "$item": "children" } }`. The repeated element renders its `children` for each item, with `$item`, `$index`, and `$bindItem` scoped to that item. Use stable, bounded arrays and keep nested expansion below renderer limits.

`watch` maps bound state paths to one action binding or an array of bindings:

```json
"watch": {
  "/form/role": { "action": "roleChanged", "params": { "source": "profile" } }
}
```

Watchers run after supported state edits for the matching bound path. Text, number, and password input controls defer watcher notification until focus leaves the field. Watchers are not a general-purpose subscription language.

## Actions and JavaScript

`Button` and `ToggleButton` may specify a string `props.action` and optional object `props.params`. On activation, Loot dispatches the action to the tool's JS runtime. Payload contains a copy of current state, a nested `params` object, and parameter keys also copied to the top level for compatibility. A watcher action uses the same dispatch path.

The plugin entry exports `globalThis.plugin` with optional `onOpen()` and `onAction(action, payload)` handlers. The host API currently includes:

```js
loot.ui.setState({ greeting: "你好" });
loot.ui.notify("已完成");
```

See the sample entry for action routing. There is no ES module loader in the current runtime. Consult `native/quickjs/loot_qjs.c` and `Loot/Plugins/QuickJsPluginScriptRuntime.cs` before depending on additional APIs, limits, or lifecycle behavior.

## Current built-in component registry

The following aliases are registered by `JsonRenderComponentRegistry.CreateDefault()` today. Aliases generally share one factory; they do not imply full compatibility with a similarly named upstream component.

| Purpose | Registered types | Common props currently read |
|---|---|---|
| Layout | `Column`, `Stack`, `VStack`; `Row`, `HStack` | `spacing` |
| Containers | `Card`; `CardHeader`, `CardFooter`; `Group`, `Panel`; `ScrollViewer`, `ScrollView` | `Card`: `title`, `description`, `padding`; scroll: `maxHeight`, `maxWidth` |
| Separator | `Divider`, `Separator` | none |
| Text/content | `Text`, `Label` | `content` or `text`, `variant`, `color` |
| Simple icon | `Icon` | `name` or `label`, `size` |
| Label chips | `Badge`, `Tag` | `label` or `text` |
| Person initials | `Avatar` | `alt` or `name`, `size` |
| Metric | `Metric` | `label`, `value`, `valueSize`, `change` |
| Actions | `Button`, `IconButton` | `label` or `text`, `align`, `enabled`, `action`, `params` |
| Toggle action | `ToggleButton` | `label`, `pressed`/`value`, optional `action` |
| Text entry | `TextField`, `TextInput`, `TextBox`, `Input` | `label`, `placeholder`, `value` |
| Multiline entry | `TextArea`, `TextAreaField`, `Textarea` | `label`, `placeholder`, `value`, `minHeight` |
| Numeric entry | `NumberField`, `NumberInput` | `label`, `placeholder`, `value` |
| Password entry | `PasswordField` | `label`, `placeholder`, `value` |
| Boolean input | `CheckBox`, `Checkbox`; `Switch`, `ToggleSwitch` | `label` or `text`, `checked`/`value`, `isThreeState` |
| Choice input | `Select`, `ComboBox`, `RadioGroup` | `options`, `value`, `label`, `placeholder` |
| Range/date | `Slider`; `DatePicker` | slider: `min`, `max`, `step`, `showTicks`, `value`; date: `value` |
| Feedback | `Progress`, `ProgressBar` | `min`, `max`, `value`, `isIndeterminate` |
| Message | `Alert` | `title`, `message` or `text`, `variant`, `padding` |

Choice `options` accepts strings or objects with `label` and `value` (the implementation also accepts `name` as a display label). Props not listed may be ignored. Verify an implementation in `Loot.JsonRender.Avalonia/Components/` before documenting or relying on it. In particular, the current `Switch` aliases render a checkbox-style control rather than a distinct platform switch widget.

## Extending the renderer

The default registry is organized by component family in `Loot.JsonRender.Avalonia/Components/LayoutComponents.cs`, `ContentComponents.cs`, `FormComponents.cs`, and `FeedbackComponents.cs`. Add a control to the closest family, keep its factory and input/state behavior local to that family, and register it through `JsonRenderComponentRegistry`. Shared property resolution, expressions, state, and element traversal belong in the renderer/context layer; avoid growing one monolithic component switch.

The registry accepts host-owned `JsonRenderComponentFactory` implementations. This is a trusted .NET extension point for Loot itself or host code. A plugin's JSON only names registered component types; it cannot define a factory, load an Avalonia assembly, or supply XAML. Keep this distinction explicit when proposing a plugin API.

When adding or changing supported JSON syntax, update the example UI and the renderer documentation/spec tests in the repository. Preserve error reporting through the renderer's error sink so malformed specs or component failures are diagnosable and remain isolated from the main application.
