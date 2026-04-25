# Obsidoc — User Manual

> Version 3 · Unity Editor tool · Namespace `Obsi.Doc`

---

## Table of Contents

1. [Overview](#overview)
2. [Quick Start](#quick-start)
3. [Attributes Reference](#attributes-reference)
4. [The Editor Window](#the-editor-window)
5. [Generated File Format](#generated-file-format)
6. [Editing Documentation](#editing-documentation)
7. [Settings](#settings)
8. [Tips & Limitations](#tips--limitations)

---

## Overview

Obsidoc is a Unity Editor tool that automatically generates Markdown documentation files from your C# types. Decorate any class, struct, enum, or interface with the `[Obsidoc]` attribute, open the **Obsi > Documentation** window, and click **Generate**. Obsidoc reflects over your assemblies, creates one `.md` file per decorated type, and keeps those files in sync every time you regenerate — without overwriting your manual edits.

The output is Obsidian-compatible Markdown and can be viewed either inside Unity (rendered view with inline editing) or directly in Obsidian.

**Supported types**

| C# type | Documented sections |
|---|---|
| `class`, `abstract class`, `sealed class` | Summary · Fields · Properties · Methods |
| `static class` | Summary · Fields · Properties · Methods |
| `struct` | Summary · Fields · Properties · Methods |
| `interface` | Summary · Properties · Methods |
| `enum` | Summary · Values |

---

## Quick Start

### 1. Decorate your type

```csharp
using Obsi.Doc;

[Obsidoc("Handles player movement and input.", category: "Gameplay", tags: new[]{"player","input"})]
public class PlayerController : MonoBehaviour
{
    [ObsidocField("Movement speed in units per second.")]
    public float speed = 5f;

    [ObsidocProperty("Whether the player is currently grounded.")]
    public bool IsGrounded { get; private set; }

    [ObsidocMethod("Moves the player in the given direction.")]
    public void Move(Vector3 direction) { }
}
```

### 2. Open the window

Go to **Obsi > Documentation** in the Unity menu bar.

### 3. Generate

Click the **Generate** button in the toolbar. Obsidoc scans all loaded assemblies, creates or updates `.md` files in the configured output folder, and archives any orphaned files.

### 4. Browse and edit

Select a file in the left panel to preview it. Use the **Rendered** toggle to switch between the formatted view and the raw Markdown. Click **Edit** to enter block-level edit mode.

---

## Attributes Reference

### `[Obsidoc]` — required

Marks a type for documentation generation.

```csharp
[Obsidoc("Short description.", category: "UI", tags: new[]{"hud","player"})]
public class HealthBar : MonoBehaviour { }
```

| Parameter | Type | Default | Description |
|---|---|---|---|
| `summary` | `string` | — | Short description of the type. Written into the `## Summary` section. |
| `category` | `string` | `""` | Classification label (e.g. `"Gameplay"`, `"Audio"`). Stored in YAML frontmatter. |
| `tags` | `string[]` | `[]` | Free-form keywords for filtering and cross-linking. |

**Valid targets:** `class`, `struct`, `enum`, `interface`

---

### `[ObsidocField]` — optional

Adds a description to a field, shown in the Description column of the **Fields** table.

```csharp
[ObsidocField("Maximum health points the entity can have.")]
public int maxHealth = 100;
```

**Valid target:** `Field`

---

### `[ObsidocProperty]` — optional

Adds a description to a property, shown in the Description column of the **Properties** table.

```csharp
[ObsidocProperty("Current health, clamped between 0 and maxHealth.")]
public int Health { get; private set; }
```

**Valid target:** `Property`  
Indexers and compiler-generated properties are always excluded.

---

### `[ObsidocMethod]` — optional

Adds a description to a method, shown below its signature block in the **Methods** section.
This description has lower priority than a manually written description in the `.md` file.

```csharp
[ObsidocMethod("Applies damage and triggers the hit reaction.")]
public void TakeDamage(int amount) { }
```

**Valid target:** `Method`

---

### `[ObsidocValue]` — optional

Adds a description to an enum member, shown in the Description column of the **Values** table.

```csharp
public enum DamageType
{
    [ObsidocValue("Standard physical damage, reduced by armor.")]
    Physical,
    [ObsidocValue("Magical damage, bypasses physical armor.")]
    Magical
}
```

**Valid target:** `Field` (enum member)

---

### `[ObsidocIncludePrivate]` — optional

Opts the type into full private-member documentation. When present, private fields, properties, and methods are included alongside public ones.

```csharp
[ObsidocIncludePrivate]
[Obsidoc("Internal state manager.")]
public class StateManager : MonoBehaviour { }
```

**Valid targets:** `class`, `struct`, `interface`

---

## The Editor Window

Open via **Obsi > Documentation**. The window is split into three areas.

```
[ Generate | Refresh                    Settings ]
[ Left panel         | | Right panel             ]
[ Status bar                                     ]
```

### Toolbar

| Button | Action |
|---|---|
| **Generate** | Scans all assemblies and creates / updates / archives `.md` files. |
| **Refresh** | Reloads the file tree and the currently selected file from disk (useful after editing in Obsidian). |
| **Settings** | Toggles the settings panel. |

### Left panel — file browser

Displays the contents of the configured Output Folder as a foldable tree.

- **Left-click** a file to select and preview it.
- **Right-click** a file for: Rename, Delete.
- **Right-click** a folder for: Create File, Create Folder, Color Folder, Rename, Delete.
- **Drag** a file or folder onto another folder to move it.
- The **search bar** at the top filters by file name or by tag (use the **Tag ▼** dropdown).

Folders can be tinted with a custom color from the palette (right-click > Color Folder).

### Right panel — preview & edit

Displays the selected `.md` file.

**Header bar**

| Control | Description |
|---|---|
| **Edit** toggle | Enters or leaves block-level edit mode. |
| **Rendered** toggle | Switches between the formatted rendered view and the raw Markdown text view (only visible outside Edit mode). |

---

## Generated File Format

Each generated file contains a YAML frontmatter block followed by Markdown sections.

```yaml
---
class: MyNamespace.PlayerController
kind: class
namespace: MyNamespace
category: Gameplay
tags: [player, input]
generated: 2026-04-14
updated: 2026-04-25
---
```

The YAML key "class" allow to select quickly the corresponding script

**YAML keys**

| Key | Source | Editable |
|---|---|---|
| `class` | Reflection | No |
| `kind` | Reflection | No |
| `namespace` | Reflection | No |
| `category` | `[Obsidoc]` attribute | No (re-synced on Generate) |
| `tags` | `[Obsidoc]` attribute | No (re-synced on Generate) |
| `generated` | First generation | No |
| `updated` | Every Generate run | No |

**Body sections by type**

| Type | Sections |
|---|---|
| class / struct | `## Summary` · `## Fields` · `## Properties` · `## Methods` |
| interface | `## Summary` · `## Properties` · `## Methods` |
| enum | `## Summary` · `## Values` |

### Orphan archiving

When a type loses its `[Obsidoc]` attribute (or is renamed / deleted), its `.md` file becomes an orphan. On the next Generate run, orphaned files are moved to the **Archive Sub-folder** instead of being deleted, so nothing is lost.

### User edit preservation

Manual edits to method descriptions and trailing content are preserved across Generate runs. Obsidoc extracts your edits before regenerating a section and re-injects them afterward. Method descriptions take priority in this order:

1. Manual description written in the `.md` file
2. `[ObsidocMethod]` attribute
3. Default placeholder `*No description.*`

---

## Editing Documentation

### Rendered view — inline editing

In the rendered view, certain elements are directly editable without switching to Edit mode.

| Element | How to edit |
|---|---|
| **Summary** paragraph | Click the text area and type directly. |
| **Method descriptions** | Click the text area below a method signature. The `*…*` italic markers are hidden while editing and restored on save. Clearing the field restores `*No description.*`. |
| **Field descriptions** | Click the Description cell in the Fields table. |
| **Property descriptions** | Click the Description cell in the Properties table. |
| **Value descriptions** | Click the Description cell in the Values table. |
| **Task checkboxes** | Toggle directly in the rendered view. |

Changes are saved to disk automatically when you stop editing.

### Edit mode — block editor

Click the **Edit** toggle in the header bar to enter block-level edit mode.

**Insert toolbar** — adds a new block after the currently selected one:

| Button | Block type |
|---|---|
| H1–H4 | Heading |
| ¶ | Paragraph |
| {} | Code block |
| — | Horizontal rule |
| > | Blockquote / callout |
| • | Unordered list item |
| 1. | Ordered list item |
| T | Table |
| [ ] | Task list |
| IMG | Image |

**Block list** — each block shows a type badge, a text preview, and a delete button.

- **Click** a block to select it and open its inline editor.
- **Drag** the handle (⠿) on the left to reorder blocks.
- `Frontmatter` and `UserContent` blocks are protected: they cannot be deleted or have their type changed.

**Task items** in the block editor support sub-tasks and indentation levels (← / →).

Click **Sauvegarder** to write changes to disk, or **Annuler** to discard them.

---

## Settings

Open via the **Settings** toolbar button. The panel has three tabs.

### Générale

| Field | Default | Description |
|---|---|---|
| Output Folder | `Assets/Documentation` | Root folder where all `.md` files are written. |
| Sub-folder | `Scripts` | Sub-folder inside Output Folder for generated files. |
| Archive Sub-folder | `_Archived` | Sub-folder for orphaned files. |
| Images Sub-folder | `_assets` | Sub-folder for images referenced in Image blocks. |
| Excluded Methods (Additionnelles) | *(empty)* | Method names to exclude from documentation, in addition to the Unity lifecycle defaults (Awake, Start, Update, etc.). |

### Style

| Field | Default | Description |
|---|---|---|
| Vue par défaut rendue | `true` | Whether the rendered view is active by default when a file is selected. |
| Titre H1 | `20` | Font size for H1 headings (range 12–32). |
| Titre H2 | `15` | Font size for H2 headings (range 10–24). |
| Titre H3 | `12` | Font size for H3 headings (range 9–18). |

### Colors

- **Palette de dossiers** — named colors that can be applied to folders via right-click > Color Folder.
- **Accents de section (H2)** — accent colors for Summary, Fields, Properties, Methods, and Values headings.
- **Modificateurs d'accès (badges H3)** — colors for the Public, Private, and Protected member badges.

---

## Tips & Limitations

**Regenerating updates only script-sourced data.**
Obsidoc re-syncs `category`, `tags`, `kind`, `namespace`, and all Fields / Properties / Methods / Values sections from reflection. Your manual edits to method descriptions, trailing Markdown, and any user-added YAML keys are always preserved.

**Static members are included.**
Both static fields and static methods are picked up automatically. Static classes are documented like any other class.

**Generic types are displayed with their type arguments.**
`List<int>`, `Dictionary<string, float>`, and nested generics like `List<List<Vector3>>` are all rendered correctly.

**Constructors and operators are not yet documented.**
Only regular methods, fields, and properties are currently included.

**The window split position is not persisted.**
The divider between the file browser and the preview resets to its default position each time the window is reopened.

**Obsidian compatibility.**
Generated files use Obsidian-flavoured Markdown: `[[wikilinks]]`, `![[embeds]]`, `> [!callout]` syntax, `==highlights==`, and `%%comments%%` are all parsed and rendered in the Unity preview. The YAML frontmatter is read by Obsidian as file properties.
