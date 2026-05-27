# UX Redesign — "Field Manual" Visual Language — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace ArkManager's "programmer-grade" UI with the distinctive "Field Manual" (C1) visual language across all 8 screens, via a centralized token/typography/icon/component theme that removes all hardcoded hex.

**Architecture:** Add four merged `ResourceDictionary` files (`Tokens`, `Typography`, `Icons`, `Controls`) under `src/ArkManager.Desktop/Themes/`, embed three open fonts, then rewrite each View's XAML to use the new style classes and English copy. All ViewModel logic is preserved; only display-only formatting helpers are added (covered by xUnit). No business-logic changes.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm 8.x, xUnit. Solution is `ArkManager.slnx`.

**Spec:** `docs/superpowers/specs/2026-05-27-ux-redesign-design.md`

---

## Ground rules (read once)

- **Avalonia 12 gotchas** (from CLAUDE.md): `Grid.RowSpacing`/`ColumnSpacing` are singular; use `TextBox.PlaceholderText` (not `Watermark`). XAML compile fails silently on bad attached props — always build after XAML edits.
- **Force dark** stays (`App.axaml RequestedThemeVariant="Dark"`). Tokens are dark-only.
- **No emoji** anywhere in final XAML. Icons come from `PathIcon` + geometry resources.
- **Preserve every `{Binding ...}`** exactly as it exists today (names verified in this plan). Restyle the chrome around them; do not rename VM members except where a task explicitly says so.
- **Commit after every task.** Branch is `main`, no remote (user pushes manually).
- Each screen task ends with a **screenshot gate** using the harness in the Appendix. Visual tweaks discovered at the gate (spacing, an icon path that renders wrong, a color that reads poorly) are fixed in-place before commit — that is expected iteration, not scope creep.

## File structure (what gets created / changed)

```
src/ArkManager.Core/
  Util/DisplayFormat.cs                 (CREATE) pure formatters
tests/ArkManager.Core.Tests/
  DisplayFormatTests.cs                 (CREATE) xUnit
src/ArkManager.Core/Services/Backups/
  BackupService.cs                      (MODIFY) add display members to BackupInfo
src/ArkManager.Desktop/
  Assets/Fonts/*.ttf                    (CREATE) embedded fonts
  Themes/Tokens.axaml                   (CREATE) color brushes
  Themes/Typography.axaml               (CREATE) fonts + TextBlock classes
  Themes/Icons.axaml                    (CREATE) geometry resources
  Themes/Controls.axaml                 (CREATE) control styles
  App.axaml                             (MODIFY) merge dictionaries, default font
  ViewModels/MainWindowViewModel.cs     (MODIFY) NavItem.Icon -> Geometry
  ViewModels/ServerViewModel.cs         (MODIFY) add Identity (display-only)
  ViewModels/BackupsViewModel.cs        (MODIFY) English copy + Summary
  Views/MainWindow.axaml                (MODIFY) shell restyle
  Views/ServerView.axaml                (REWRITE)
  Views/RconView.axaml                  (REWRITE)
  Views/ConfigView.axaml                (REWRITE)
  Views/ModsView.axaml                  (REWRITE)
  Views/BackupsView.axaml               (REWRITE)
  Views/DoctorView.axaml                (REWRITE)
  Views/InstallView.axaml               (REWRITE)
  Views/SettingsView.axaml              (REWRITE)
```

---

## Task 1: Pure display formatters (TDD)

Independent of all UI. Pure functions → full TDD.

**Files:**
- Create: `src/ArkManager.Core/Util/DisplayFormat.cs`
- Test: `tests/ArkManager.Core.Tests/DisplayFormatTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ArkManager.Core.Util;

namespace ArkManager.Core.Tests;

public class DisplayFormatTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(69_273_600L, "66.1 MB")]
    [InlineData(1_288_490_188L, "1.2 GB")]
    public void HumanSize_formats_bytes(long bytes, string expected)
        => Assert.Equal(expected, DisplayFormat.HumanSize(bytes));

    [Fact]
    public void RelativeTime_today_shows_time()
    {
        var now = new DateTime(2026, 5, 28, 23, 40, 0, DateTimeKind.Utc);
        var v   = new DateTime(2026, 5, 28, 23, 28, 0, DateTimeKind.Utc);
        Assert.Equal("today, 23:28", DisplayFormat.RelativeTime(v, now));
    }

    [Fact]
    public void RelativeTime_yesterday_shows_time()
    {
        var now = new DateTime(2026, 5, 28, 1, 0, 0, DateTimeKind.Utc);
        var v   = new DateTime(2026, 5, 27, 22, 55, 0, DateTimeKind.Utc);
        Assert.Equal("yesterday, 22:55", DisplayFormat.RelativeTime(v, now));
    }

    [Fact]
    public void RelativeTime_older_shows_days_ago()
    {
        var now = new DateTime(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc);
        var v   = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("3 days ago", DisplayFormat.RelativeTime(v, now));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArkManager.slnx --filter DisplayFormatTests`
Expected: FAIL — `DisplayFormat` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Globalization;

namespace ArkManager.Core.Util;

/// <summary>Чистые форматтеры для UI: человекочитаемые размер/время. Без зависимостей от UI.</summary>
public static class DisplayFormat
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string HumanSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return string.Create(CultureInfo.InvariantCulture, $"{v:0.0} {Units[u]}");
    }

    /// <summary>"today, 23:28" / "yesterday, 22:55" / "3 days ago". Локальное время для отображения.</summary>
    public static string RelativeTime(DateTime valueUtc, DateTime nowUtc)
    {
        var local = valueUtc.ToLocalTime();
        var today = nowUtc.ToLocalTime().Date;
        var day = local.Date;
        if (day == today) return $"today, {local:HH:mm}";
        if (day == today.AddDays(-1)) return $"yesterday, {local:HH:mm}";
        var days = (today - day).Days;
        return $"{days} days ago";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArkManager.slnx --filter DisplayFormatTests`
Expected: PASS (all cases). If `HumanSize(69_273_600)` differs, adjust the expected constant to match the rounding — the algorithm is authoritative, fix the test data, not the formula.

- [ ] **Step 5: Commit**

```bash
git add src/ArkManager.Core/Util/DisplayFormat.cs tests/ArkManager.Core.Tests/DisplayFormatTests.cs
git commit -m "feat(core): pure display formatters (HumanSize, RelativeTime)"
```

---

## Task 2: Embed fonts

Download three OFL fonts and embed them so the app has no system-font dependency.

**Files:**
- Create: `src/ArkManager.Desktop/Assets/Fonts/ZillaSlab-SemiBold.ttf`, `ZillaSlab-Bold.ttf`, `IBMPlexSans-Regular.ttf`, `IBMPlexSans-Medium.ttf`, `IBMPlexSans-SemiBold.ttf`, `IBMPlexMono-Regular.ttf`, `IBMPlexMono-Medium.ttf`
- Modify: `src/ArkManager.Desktop/ArkManager.App.csproj`

- [ ] **Step 1: Fetch the fonts**

```bash
mkdir -p src/ArkManager.Desktop/Assets/Fonts
cd src/ArkManager.Desktop/Assets/Fonts
base=https://github.com/google/fonts/raw/main
curl -L -o ZillaSlab-SemiBold.ttf   "$base/ofl/zillaslab/ZillaSlab-SemiBold.ttf"
curl -L -o ZillaSlab-Bold.ttf       "$base/ofl/zillaslab/ZillaSlab-Bold.ttf"
curl -L -o IBMPlexSans-Regular.ttf  "$base/ofl/ibmplexsans/IBMPlexSans-Regular.ttf"
curl -L -o IBMPlexSans-Medium.ttf   "$base/ofl/ibmplexsans/IBMPlexSans-Medium.ttf"
curl -L -o IBMPlexSans-SemiBold.ttf "$base/ofl/ibmplexsans/IBMPlexSans-SemiBold.ttf"
curl -L -o IBMPlexMono-Regular.ttf  "$base/ofl/ibmplexmono/IBMPlexMono-Regular.ttf"
curl -L -o IBMPlexMono-Medium.ttf   "$base/ofl/ibmplexmono/IBMPlexMono-Medium.ttf"
cd -
file src/ArkManager.Desktop/Assets/Fonts/*.ttf
```

Expected: each file reports `TrueType Font data`. If a URL 404s (Google Fonts repo layout changed), find the correct path under `https://github.com/google/fonts/tree/main/ofl/<family>` and re-fetch. Do not proceed with HTML error pages saved as `.ttf`.

- [ ] **Step 2: Confirm fonts are bundled as AvaloniaResource**

The csproj already has `<AvaloniaResource Include="Assets\**" />`, so the ttf files are bundled automatically — no csproj edit needed. Verify the glob covers them:

Run: `grep -n "AvaloniaResource" src/ArkManager.Desktop/ArkManager.App.csproj`
Expected: `<AvaloniaResource Include="Assets\**" />` present. If absent, add it inside the existing `<ItemGroup>` that has `<Folder Include="Models\" />`.

- [ ] **Step 3: Build to confirm resources compile**

Run: `dotnet build ArkManager.slnx 2>&1 | tail -5`
Expected: `Сборка успешно завершена` / Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ArkManager.Desktop/Assets/Fonts/ src/ArkManager.Desktop/ArkManager.App.csproj
git commit -m "chore(ui): embed Zilla Slab + IBM Plex Sans/Mono fonts"
```

---

## Task 3: Tokens.axaml — color system

**Files:**
- Create: `src/ArkManager.Desktop/Themes/Tokens.axaml`

- [ ] **Step 1: Create the dictionary**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Field Manual (C1): тёплый уголь + bone-текст + ember-янтарь. Dark-only. -->
    <SolidColorBrush x:Key="BgBrush"        Color="#1a1611"/>
    <SolidColorBrush x:Key="RailBrush"      Color="#15110c"/>
    <SolidColorBrush x:Key="PanelBrush"     Color="#221d15"/>
    <SolidColorBrush x:Key="Panel2Brush"    Color="#1d1810"/>
    <SolidColorBrush x:Key="ConsoleBrush"   Color="#100d09"/>
    <SolidColorBrush x:Key="LineBrush"      Color="#332a1d"/>
    <SolidColorBrush x:Key="Line2Brush"     Color="#473a27"/>
    <SolidColorBrush x:Key="TextBrush"      Color="#ece3d3"/>
    <SolidColorBrush x:Key="MutedBrush"     Color="#9c8d72"/>
    <SolidColorBrush x:Key="AccentBrush"    Color="#e08a2b"/>
    <SolidColorBrush x:Key="AccentOnBrush"  Color="#1a1207"/>
    <SolidColorBrush x:Key="AccentInkBrush" Color="#f2a64a"/>
    <SolidColorBrush x:Key="AccentSoftBrush" Color="#22e08a2b"/>  <!-- ~13% alpha -->
    <SolidColorBrush x:Key="AccentGlowBrush" Color="#4ce08a2b"/>
    <SolidColorBrush x:Key="OkBrush"        Color="#8fbf52"/>
    <SolidColorBrush x:Key="OkSoftBrush"    Color="#228fbf52"/>
    <SolidColorBrush x:Key="OkLineBrush"    Color="#4c8fbf52"/>
    <SolidColorBrush x:Key="DangerBrush"    Color="#d9685a"/>
    <SolidColorBrush x:Key="WarnBrush"      Color="#d9a441"/>
</ResourceDictionary>
```

(`#22e08a2b` = ARGB: alpha `0x22`≈13%, RGB `e08a2b`.)

- [ ] **Step 2: Commit** (wired into App.axaml in Task 6)

```bash
git add src/ArkManager.Desktop/Themes/Tokens.axaml
git commit -m "feat(ui): Tokens.axaml color system (Field Manual)"
```

---

## Task 4: Typography.axaml — fonts + text classes

**Files:**
- Create: `src/ArkManager.Desktop/Themes/Typography.axaml`

- [ ] **Step 1: Create the dictionary**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <FontFamily x:Key="DisplayFont">avares://ArkManager.App/Assets/Fonts/#Zilla Slab</FontFamily>
    <FontFamily x:Key="UiFont">avares://ArkManager.App/Assets/Fonts/#IBM Plex Sans</FontFamily>
    <FontFamily x:Key="MonoFont">avares://ArkManager.App/Assets/Fonts/#IBM Plex Mono</FontFamily>

    <Styles>
        <!-- Page title -->
        <Style Selector="TextBlock.h1">
            <Setter Property="FontFamily" Value="{StaticResource DisplayFont}"/>
            <Setter Property="FontWeight" Value="Bold"/>
            <Setter Property="FontSize" Value="25"/>
            <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
        </Style>
        <!-- Big numeric value (vitals) -->
        <Style Selector="TextBlock.stat">
            <Setter Property="FontFamily" Value="{StaticResource DisplayFont}"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="FontSize" Value="22"/>
            <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
        </Style>
        <!-- Uppercase mono section label -->
        <Style Selector="TextBlock.section">
            <Setter Property="FontFamily" Value="{StaticResource MonoFont}"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}"/>
            <Setter Property="LetterSpacing" Value="2"/>
        </Style>
        <!-- Mono caption / meta -->
        <Style Selector="TextBlock.meta">
            <Setter Property="FontFamily" Value="{StaticResource MonoFont}"/>
            <Setter Property="FontSize" Value="11"/>
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}"/>
        </Style>
    </Styles>
</ResourceDictionary>
```

Note: Avalonia `LetterSpacing` is in device-independent units (not em). `2` ≈ subtle tracking at 10px. Adjust at the Task 6 gate if it looks too wide.

- [ ] **Step 2: Commit**

```bash
git add src/ArkManager.Desktop/Themes/Typography.axaml
git commit -m "feat(ui): Typography.axaml (embedded fonts + text classes)"
```

---

## Task 5: Icons.axaml — geometry resources

All icons are **solid silhouettes** used via `PathIcon` (fills with inherited `Foreground`). Coordinates are in a 24×24 space.

**Files:**
- Create: `src/ArkManager.Desktop/Themes/Icons.axaml`

- [ ] **Step 1: Create the dictionary**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- nav -->
    <StreamGeometry x:Key="IconServer">M7 5 L19 12 L7 19 Z</StreamGeometry>
    <StreamGeometry x:Key="IconRcon">M3 5 H21 V19 H3 Z M6 9 L10 12 L6 15 V13 L8 12 L6 11 Z M12 14 H17 V16 H12 Z</StreamGeometry>
    <StreamGeometry x:Key="IconInstall">M11 4 H13 V11 H16 L12 16 L8 11 H11 Z M5 18 H19 V20 H5 Z</StreamGeometry>
    <StreamGeometry x:Key="IconConfig">M3 6 H21 V8 H3 Z M3 11 H21 V13 H3 Z M3 16 H15 V18 H3 Z</StreamGeometry>
    <StreamGeometry x:Key="IconMods">M12 3 L20 7 V17 L12 21 L4 17 V7 Z M12 8 L16 10 V14 L12 16 L8 14 V10 Z</StreamGeometry>
    <StreamGeometry x:Key="IconBackups">M4 4 H20 V8 H4 Z M5 9 H19 V20 H5 Z M9 12 H15 V14 H9 Z</StreamGeometry>
    <StreamGeometry x:Key="IconDoctor">M10 3 H14 V9 H20 V13 H14 V21 H10 V13 H4 V9 H10 Z</StreamGeometry>
    <StreamGeometry x:Key="IconSettings">M10 2 H14 L14.6 4.6 L17 5.6 L19.4 4.4 L21.6 6.6 L20.4 9 L21.4 11.4 L24 12 V16 L21.4 16.6 L20.4 19 L21.6 21.4 ... Z</StreamGeometry>
    <!-- actions -->
    <StreamGeometry x:Key="IconPlay">M7 5 L19 12 L7 19 Z</StreamGeometry>
    <StreamGeometry x:Key="IconStop">M7 7 H17 V17 H7 Z</StreamGeometry>
    <StreamGeometry x:Key="IconPlus">M11 5 H13 V11 H19 V13 H13 V19 H11 V13 H5 V11 H11 Z</StreamGeometry>
    <StreamGeometry x:Key="IconCopy">M9 9 H19 V20 H9 Z M6 4 H16 V6 H8 V16 H6 Z</StreamGeometry>
    <StreamGeometry x:Key="IconTrash">M6 7 H18 L17 20 H7 Z M9 5 H15 V7 H9 Z</StreamGeometry>
    <StreamGeometry x:Key="IconFolder">M3 6 H10 L12 8 H21 V19 H3 Z</StreamGeometry>
    <StreamGeometry x:Key="IconRefresh">M12 5 A7 7 0 1 1 5 12 H7 A5 5 0 1 0 12 7 V10 L8 6 L12 2 Z</StreamGeometry>
    <StreamGeometry x:Key="IconClock">M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 Z M11 7 H13 V12 L16 14 L15 16 L11 13 Z</StreamGeometry>
    <StreamGeometry x:Key="IconArrowUp">M12 5 L18 12 H14 V19 H10 V12 H6 Z</StreamGeometry>
    <StreamGeometry x:Key="IconArrowDown">M12 19 L6 12 H10 V5 H14 V12 H18 Z</StreamGeometry>
    <StreamGeometry x:Key="IconGlobe">M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 Z M4 12 H20 M12 3 C8 7 8 17 12 21 C16 17 16 7 12 3 Z</StreamGeometry>
    <StreamGeometry x:Key="IconSave">M5 5 H16 L19 8 V19 H5 Z M8 5 H14 V9 H8 Z M8 13 H16 V18 H8 Z</StreamGeometry>
</ResourceDictionary>
```

- [ ] **Step 2: Fix the Settings gear geometry**

`IconSettings` above is intentionally incomplete (`...`). Replace it with a clean, closed gear path. A simple, reliable 8-notch gear is hard to hand-author; use this tested cog-ring instead (outer ring + inner hole reads as a gear at 17px):

```xml
    <StreamGeometry x:Key="IconSettings">M12 2 L14 4 H16.5 L17 6.5 L19.5 7.5 L19 10 L21 12 L19 14 L19.5 16.5 L17 17.5 L16.5 20 L14 20 L12 22 L10 20 L7.5 20 L7 17.5 L4.5 16.5 L5 14 L3 12 L5 10 L4.5 7.5 L7 6.5 L7.5 4 L10 4 Z M12 8 A4 4 0 1 0 12 16 A4 4 0 1 0 12 8 Z</StreamGeometry>
```

- [ ] **Step 3: Verify geometries render (gate)**

This is the only way to validate hand-authored path data. Build, run, and screenshot the nav (icons are wired in Task 6, so this gate is effectively performed at the end of Task 6). For now just confirm the XAML parses:

Run: `dotnet build ArkManager.slnx 2>&1 | tail -5`
Expected: Build succeeded. A malformed `StreamGeometry` string fails the build with a parse error — fix the offending path before continuing.

- [ ] **Step 4: Commit**

```bash
git add src/ArkManager.Desktop/Themes/Icons.axaml
git commit -m "feat(ui): Icons.axaml solid glyph geometries"
```

---

## Task 6: Controls.axaml + wire dictionaries into App.axaml + restyle shell

This is the biggest foundational task: the component styles, the dictionary wiring, the default font, and the MainWindow/nav restyle (which is also where icons get visually verified).

**Files:**
- Create: `src/ArkManager.Desktop/Themes/Controls.axaml`
- Modify: `src/ArkManager.Desktop/App.axaml`
- Modify: `src/ArkManager.Desktop/ViewModels/MainWindowViewModel.cs`
- Modify: `src/ArkManager.Desktop/Views/MainWindow.axaml`

- [ ] **Step 1: Create Controls.axaml**

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Styles>
        <!-- ===== Buttons ===== -->
        <Style Selector="Button">
            <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Padding" Value="14,9"/>
            <Setter Property="CornerRadius" Value="9"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="BorderBrush" Value="{StaticResource Line2Brush}"/>
        </Style>
        <Style Selector="Button:pointerover /template/ ContentPresenter">
            <Setter Property="Background" Value="{StaticResource Panel2Brush}"/>
        </Style>
        <Style Selector="Button.primary">
            <Setter Property="Background" Value="{StaticResource AccentBrush}"/>
            <Setter Property="Foreground" Value="{StaticResource AccentOnBrush}"/>
            <Setter Property="BorderThickness" Value="0"/>
        </Style>
        <Style Selector="Button.primary:pointerover /template/ ContentPresenter">
            <Setter Property="Background" Value="{StaticResource AccentInkBrush}"/>
        </Style>
        <Style Selector="Button.icon">
            <Setter Property="Padding" Value="0"/>
            <Setter Property="Width" Value="38"/>
            <Setter Property="Height" Value="38"/>
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}"/>
        </Style>
        <Style Selector="Button.danger">
            <Setter Property="Foreground" Value="{StaticResource DangerBrush}"/>
            <Setter Property="BorderBrush" Value="#4cd9685a"/>
        </Style>
        <Style Selector="Button.chip">
            <Setter Property="FontFamily" Value="{StaticResource MonoFont}"/>
            <Setter Property="FontWeight" Value="Medium"/>
            <Setter Property="FontSize" Value="12"/>
            <Setter Property="Padding" Value="10,7"/>
        </Style>

        <!-- PathIcon default sizing inside controls -->
        <Style Selector="PathIcon">
            <Setter Property="Width" Value="15"/>
            <Setter Property="Height" Value="15"/>
        </Style>

        <!-- ===== Panels / tiles ===== -->
        <Style Selector="Border.panel">
            <Setter Property="Background" Value="{StaticResource PanelBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource LineBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="11"/>
            <Setter Property="Padding" Value="18"/>
        </Style>
        <Style Selector="Border.tile">
            <Setter Property="Background" Value="{StaticResource PanelBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource LineBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="10"/>
            <Setter Property="Padding" Value="12,12"/>
        </Style>
        <Style Selector="Border.console">
            <Setter Property="Background" Value="{StaticResource ConsoleBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource LineBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="11"/>
        </Style>

        <!-- ===== Chips / pills ===== -->
        <Style Selector="Border.chip">
            <Setter Property="CornerRadius" Value="999"/>
            <Setter Property="Padding" Value="9,6"/>
            <Setter Property="Background" Value="{StaticResource OkSoftBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource OkLineBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
        </Style>
        <Style Selector="Border.pill">
            <Setter Property="CornerRadius" Value="999"/>
            <Setter Property="Padding" Value="9,6"/>
            <Setter Property="Background" Value="{StaticResource AccentSoftBrush}"/>
            <Setter Property="BorderBrush" Value="#40e08a2b"/>
            <Setter Property="BorderThickness" Value="1"/>
        </Style>

        <!-- ===== Inputs ===== -->
        <Style Selector="TextBox">
            <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Background" Value="{StaticResource Panel2Brush}"/>
            <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource Line2Brush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="8"/>
            <Setter Property="Padding" Value="9,8"/>
        </Style>
        <Style Selector="TextBox:focus /template/ Border#PART_BorderElement">
            <Setter Property="BorderBrush" Value="{StaticResource AccentBrush}"/>
        </Style>
        <Style Selector="TextBox.mono">
            <Setter Property="FontFamily" Value="{StaticResource MonoFont}"/>
        </Style>
        <Style Selector="NumericUpDown">
            <Setter Property="Background" Value="{StaticResource Panel2Brush}"/>
            <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource Line2Brush}"/>
            <Setter Property="CornerRadius" Value="8"/>
        </Style>
        <Style Selector="ComboBox">
            <Setter Property="Background" Value="{StaticResource Panel2Brush}"/>
            <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource Line2Brush}"/>
            <Setter Property="CornerRadius" Value="8"/>
        </Style>
        <Style Selector="ToggleSwitch">
            <Setter Property="Foreground" Value="{StaticResource TextBrush}"/>
        </Style>
        <Style Selector="ToggleSwitch:checked /template/ Border#SwitchKnobBounds">
            <Setter Property="Background" Value="{StaticResource AccentBrush}"/>
        </Style>

        <!-- ===== ScrollViewer / ProgressBar ===== -->
        <Style Selector="ProgressBar">
            <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
            <Setter Property="Background" Value="{StaticResource LineBrush}"/>
        </Style>

        <!-- ===== Nav ListBox ===== -->
        <Style Selector="ListBox.nav">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
        </Style>
        <Style Selector="ListBox.nav ListBoxItem">
            <Setter Property="Padding" Value="11,9"/>
            <Setter Property="Margin" Value="0,1"/>
            <Setter Property="CornerRadius" Value="8"/>
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}"/>
            <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
            <Setter Property="FontSize" Value="14"/>
        </Style>
        <Style Selector="ListBox.nav ListBoxItem:selected /template/ ContentPresenter">
            <Setter Property="Background" Value="{StaticResource AccentSoftBrush}"/>
        </Style>
        <Style Selector="ListBox.nav ListBoxItem:selected">
            <Setter Property="Foreground" Value="{StaticResource AccentInkBrush}"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
        </Style>

        <!-- ===== Generic list rows (Backups/Mods) ===== -->
        <Style Selector="ListBox.rows">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
        </Style>
        <Style Selector="ListBox.rows ListBoxItem">
            <Setter Property="Padding" Value="0"/>
            <Setter Property="Margin" Value="0,0,0,8"/>
            <Setter Property="Background" Value="Transparent"/>
        </Style>

        <!-- ===== Config tab strip (segmented look) ===== -->
        <Style Selector="TabControl.seg">
            <Setter Property="Background" Value="Transparent"/>
        </Style>
        <Style Selector="TabControl.seg TabItem">
            <Setter Property="FontFamily" Value="{StaticResource UiFont}"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Foreground" Value="{StaticResource MutedBrush}"/>
            <Setter Property="Padding" Value="13,7"/>
            <Setter Property="Margin" Value="0,0,2,0"/>
            <Setter Property="MinHeight" Value="0"/>
        </Style>
        <Style Selector="TabControl.seg TabItem:selected">
            <Setter Property="Foreground" Value="{StaticResource AccentInkBrush}"/>
        </Style>
    </Styles>
</ResourceDictionary>
```

Note: control-template part names (`PART_BorderElement`, `SwitchKnobBounds`) match Avalonia 12 FluentTheme. If a `/template/` selector fails to apply at the gate, inspect the actual template part name and adjust — these are the only fragile selectors here.

- [ ] **Step 2: Wire dictionaries + default font into App.axaml**

Replace the whole `App.axaml` with:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="ArkManager.App.App"
             xmlns:local="using:ArkManager.App"
             RequestedThemeVariant="Dark">

    <Application.DataTemplates>
        <local:ViewLocator/>
    </Application.DataTemplates>

    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="avares://ArkManager.App/Themes/Tokens.axaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>

    <Application.Styles>
        <FluentTheme />
        <StyleInclude Source="avares://ArkManager.App/Themes/Typography.axaml"/>
        <StyleInclude Source="avares://ArkManager.App/Themes/Controls.axaml"/>
        <Style Selector="Window">
            <Setter Property="Background" Value="{DynamicResource BgBrush}"/>
            <Setter Property="FontFamily" Value="{DynamicResource UiFont}"/>
        </Style>
    </Application.Styles>
</Application>
```

Wait — `Typography.axaml` and `Controls.axaml` mix `<FontFamily>`/`<StreamGeometry>` resources with `<Styles>`. A `StyleInclude` source must have `<Styles>` as root; a `ResourceInclude` must have `<ResourceDictionary>` root. **Resolve this now:** Typography/Controls/Icons each contain BOTH resources and styles. Split responsibility cleanly:

- `Tokens.axaml`, `Icons.axaml`, and the FontFamily entries → **resources** (ResourceDictionary root, merged via `ResourceInclude`).
- The `<Style>` blocks → **styles** (Styles root, included via `StyleInclude`).

So restructure: move the `<FontFamily>` keys from Typography.axaml into a resources dictionary, and keep only `<Style>` blocks in style files. Final layout:

- `Themes/Resources.axaml` — `<ResourceDictionary>` merging Tokens + Icons + FontFamily keys (one file with `MergedDictionaries` for Tokens/Icons plus the three `<FontFamily>` entries inline).
- `Themes/TextStyles.axaml` — `<Styles>` root with the `TextBlock.*` styles (was Typography styles).
- `Themes/Controls.axaml` — `<Styles>` root (as written above).

Rewrite `Themes/Typography.axaml` → split: delete the `<Styles>`/`<FontFamily>` mix. Create `Themes/Resources.axaml`:

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://ArkManager.App/Themes/Tokens.axaml"/>
        <ResourceInclude Source="avares://ArkManager.App/Themes/Icons.axaml"/>
    </ResourceDictionary.MergedDictionaries>
    <FontFamily x:Key="DisplayFont">avares://ArkManager.App/Assets/Fonts/#Zilla Slab</FontFamily>
    <FontFamily x:Key="UiFont">avares://ArkManager.App/Assets/Fonts/#IBM Plex Sans</FontFamily>
    <FontFamily x:Key="MonoFont">avares://ArkManager.App/Assets/Fonts/#IBM Plex Mono</FontFamily>
</ResourceDictionary>
```

Create `Themes/TextStyles.axaml` = a `<Styles>` root containing exactly the four `TextBlock.*` `<Style>` blocks from Task 4 Step 1 (the `<FontFamily>` keys removed). And set App.axaml to:

```xml
    <Application.Resources>
        <ResourceInclude Source="avares://ArkManager.App/Themes/Resources.axaml"/>
    </Application.Resources>

    <Application.Styles>
        <FluentTheme />
        <StyleInclude Source="avares://ArkManager.App/Themes/TextStyles.axaml"/>
        <StyleInclude Source="avares://ArkManager.App/Themes/Controls.axaml"/>
        <Style Selector="Window">
            <Setter Property="Background" Value="{DynamicResource BgBrush}"/>
            <Setter Property="FontFamily" Value="{DynamicResource UiFont}"/>
        </Style>
    </Application.Styles>
```

(Delete `Themes/Typography.axaml`; its content is now split between `Resources.axaml` and `TextStyles.axaml`. Update Task 4's commit note mentally — the file just got renamed/split.)

- [ ] **Step 3: Change NavItem.Icon to Geometry**

In `ViewModels/MainWindowViewModel.cs`, change the record and the nav construction:

```csharp
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkManager.App.ViewModels;

public sealed record NavItem(string Title, Geometry Icon, ViewModelBase ViewModel);

public partial class MainWindowViewModel : ViewModelBase
{
    // Solid glyph paths (mirror Themes/Icons.axaml). 24x24 space.
    private static Geometry G(string path) => Geometry.Parse(path);

    public ObservableCollection<NavItem> NavItems { get; }
    // ... (rest unchanged except the NavItems initializer below)
```

Replace the `NavItems = new ObservableCollection<NavItem> { ... }` initializer with:

```csharp
        NavItems = new ObservableCollection<NavItem>
        {
            new("Server",   G("M7 5 L19 12 L7 19 Z"), server),
            new("RCON",     G("M3 5 H21 V19 H3 Z M6 9 L10 12 L6 15 V13 L8 12 L6 11 Z M12 14 H17 V16 H12 Z"), rcon),
            new("Install",  G("M11 4 H13 V11 H16 L12 16 L8 11 H11 Z M5 18 H19 V20 H5 Z"), install),
            new("Config",   G("M3 6 H21 V8 H3 Z M3 11 H21 V13 H3 Z M3 16 H15 V18 H3 Z"), config),
            new("Mods",     G("M12 3 L20 7 V17 L12 21 L4 17 V7 Z M12 8 L16 10 V14 L12 16 L8 14 V10 Z"), mods),
            new("Backups",  G("M4 4 H20 V8 H4 Z M5 9 H19 V20 H5 Z M9 12 H15 V14 H9 Z"), backups),
            new("Doctor",   G("M10 3 H14 V9 H20 V13 H14 V21 H10 V13 H4 V9 H10 Z"), doctor),
            new("Settings", G("M12 2 L14 4 H16.5 L17 6.5 L19.5 7.5 L19 10 L21 12 L19 14 L19.5 16.5 L17 17.5 L16.5 20 L14 20 L12 22 L10 20 L7.5 20 L7 17.5 L4.5 16.5 L5 14 L3 12 L5 10 L4.5 7.5 L7 6.5 L7.5 4 L10 4 Z M12 8 A4 4 0 1 0 12 16 A4 4 0 1 0 12 8 Z"), settings),
        };
```

- [ ] **Step 4: Restyle MainWindow.axaml**

Replace `Views/MainWindow.axaml` with:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:ArkManager.App.ViewModels"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="1100" d:DesignHeight="720"
        Width="1100" Height="720" MinWidth="900" MinHeight="600"
        x:Class="ArkManager.App.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Icon="/Assets/AppIcon.png"
        Title="ArkManager — ASA Server Manager">
    <Grid ColumnDefinitions="216,*">
        <Border Grid.Column="0" Background="{DynamicResource RailBrush}"
                BorderBrush="{DynamicResource LineBrush}" BorderThickness="0,0,1,0" Padding="12,18">
            <DockPanel>
                <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="10" Margin="6,0,6,18">
                    <PathIcon Data="{DynamicResource IconMods}" Width="26" Height="26"
                              Foreground="{DynamicResource AccentBrush}"/>
                    <StackPanel>
                        <TextBlock Text="ArkManager" FontFamily="{DynamicResource DisplayFont}"
                                   FontWeight="Bold" FontSize="17" Foreground="{DynamicResource TextBrush}"/>
                        <TextBlock Text="ASA CONTROL" Classes="meta" FontSize="9" LetterSpacing="2"/>
                    </StackPanel>
                </StackPanel>
                <ListBox Classes="nav" ItemsSource="{Binding NavItems}"
                         SelectedItem="{Binding Selected, Mode=TwoWay}">
                    <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="vm:NavItem">
                            <StackPanel Orientation="Horizontal" Spacing="11">
                                <PathIcon Data="{Binding Icon}" Width="17" Height="17"/>
                                <TextBlock Text="{Binding Title}" VerticalAlignment="Center"/>
                            </StackPanel>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </DockPanel>
        </Border>
        <ContentControl Grid.Column="1" Content="{Binding CurrentPage}" Margin="22"/>
    </Grid>
</Window>
```

(Doctor/Settings bottom-pinning: the nav is a single bound ListBox, so true bottom-pinning would require splitting NavItems. Out of scope for this pass — keep all 8 in one list. Revisit only if the user asks.)

- [ ] **Step 5: Build + run + screenshot gate**

```bash
dotnet build ArkManager.slnx 2>&1 | tail -5
```
Expected: Build succeeded.

Then run the screenshot harness (Appendix A) and capture the Server tab. **Verify:** warm charcoal background, ember sidebar active item, nav icons render as recognizable solid glyphs (NOT blobs). If any nav glyph is unrecognizable, fix its path string in BOTH `Icons.axaml` and the `MainWindowViewModel` initializer, rebuild, re-shoot. The Settings gear is the most likely to need adjustment.

- [ ] **Step 6: Commit**

```bash
git add src/ArkManager.Desktop/Themes/ src/ArkManager.Desktop/App.axaml \
        src/ArkManager.Desktop/ViewModels/MainWindowViewModel.cs \
        src/ArkManager.Desktop/Views/MainWindow.axaml
git commit -m "feat(ui): theme dictionaries wired + Field Manual shell/nav"
```

---

## Task 7: ServerView

**Files:**
- Modify: `src/ArkManager.Desktop/ViewModels/ServerViewModel.cs` (add display-only `Identity`)
- Rewrite: `src/ArkManager.Desktop/Views/ServerView.axaml`

- [ ] **Step 1: Add `Identity` to ServerViewModel**

ServerViewModel currently has no session/map info. Inject `SettingsService` to surface a header subtitle. Add a field + property; do not touch existing logic.

In `ServerViewModel.cs`, add `using ArkManager.Core.Services;` (already present), add a settings field and set Identity in the real constructor:

```csharp
    [ObservableProperty] private string _identity = "—";
```

Change the real constructor signature and body start:

```csharp
    public ServerViewModel(ServerManager server, PlayerPoller poller, SettingsService settings)
    {
        _server = server;
        var s = settings.Current;
        Identity = $"{s.SessionName} · {s.Map}";
        // ... rest unchanged
```

Update the designer ctor call `new ServerViewModel()` stays valid (it's parameterless). Verify DI: `SettingsService` is already registered (used elsewhere), so constructor injection resolves. Confirm property names `SessionName` and `Map` exist on `AppSettings`:

Run: `grep -nE "SessionName|public string Map" src/ArkManager.Core/Models/AppSettings.cs`
Expected: both present. If `Map` is named differently (e.g., `MapId`), use that name.

- [ ] **Step 2: Rewrite ServerView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArkManager.App.ViewModels"
             x:Class="ArkManager.App.Views.ServerView"
             x:DataType="vm:ServerViewModel">
    <Grid RowDefinitions="auto,auto,auto,*" RowSpacing="16">
        <!-- header -->
        <Grid Grid.Row="0" ColumnDefinitions="*,auto">
            <StackPanel Grid.Column="0" Spacing="6">
                <TextBlock Classes="h1" Text="Server"/>
                <TextBlock Classes="meta" Text="{Binding Identity}"/>
            </StackPanel>
            <Border Grid.Column="1" Classes="chip" VerticalAlignment="Center">
                <StackPanel Orientation="Horizontal" Spacing="7">
                    <Ellipse Width="8" Height="8" Fill="{Binding StatusBrush}" VerticalAlignment="Center"/>
                    <TextBlock Text="{Binding StatusText}" FontWeight="SemiBold" FontSize="12"
                               Foreground="{DynamicResource TextBrush}"/>
                </StackPanel>
            </Border>
        </Grid>

        <!-- actions -->
        <Grid Grid.Row="1" ColumnDefinitions="auto,auto,*,auto,auto,200">
            <Button Grid.Column="0" Classes="primary" Command="{Binding StartCommand}" Margin="0,0,10,0">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <PathIcon Data="{DynamicResource IconPlay}"/><TextBlock Text="Start server"/>
                </StackPanel>
            </Button>
            <Button Grid.Column="1" Command="{Binding StopCommand}" Margin="0,0,10,0">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <PathIcon Data="{DynamicResource IconStop}"/><TextBlock Text="Stop"/>
                </StackPanel>
            </Button>
            <Button Grid.Column="3" Classes="icon" Command="{Binding CopyLogCommand}" ToolTip.Tip="Copy log" Margin="0,0,6,0">
                <PathIcon Data="{DynamicResource IconCopy}"/>
            </Button>
            <Button Grid.Column="4" Classes="icon" Command="{Binding ClearLogCommand}" ToolTip.Tip="Clear log" Margin="0,0,10,0">
                <PathIcon Data="{DynamicResource IconTrash}"/>
            </Button>
            <TextBox Grid.Column="5" Text="{Binding Filter}" PlaceholderText="Filter log…"/>
        </Grid>

        <!-- vitals -->
        <Grid Grid.Row="2" ColumnDefinitions="*,*,*,*" ColumnSpacing="10">
            <Border Grid.Column="0" Classes="tile">
                <StackPanel Spacing="5"><TextBlock Classes="section" Text="UPTIME"/><TextBlock Classes="stat" Text="{Binding Uptime}"/></StackPanel>
            </Border>
            <Border Grid.Column="1" Classes="tile">
                <StackPanel Spacing="5"><TextBlock Classes="section" Text="CPU"/><TextBlock Classes="stat" Text="{Binding CpuUsage}"/></StackPanel>
            </Border>
            <Border Grid.Column="2" Classes="tile">
                <StackPanel Spacing="5"><TextBlock Classes="section" Text="MEMORY"/><TextBlock Classes="stat" Text="{Binding RamUsage}"/></StackPanel>
            </Border>
            <Border Grid.Column="3" Classes="tile">
                <StackPanel Spacing="5">
                    <TextBlock Classes="section" Text="PLAYERS"/>
                    <TextBlock Classes="stat" Text="{Binding PlayersOnline}"/>
                    <TextBlock Classes="meta" Text="{Binding PlayersDetail}" TextTrimming="CharacterEllipsis"/>
                </StackPanel>
            </Border>
        </Grid>

        <!-- console -->
        <Border Grid.Row="3" Classes="console">
            <Grid RowDefinitions="auto,*">
                <Grid Grid.Row="0" ColumnDefinitions="*,auto" Margin="14,9">
                    <TextBlock Grid.Column="0" Classes="section" Text="CONSOLE" VerticalAlignment="Center"/>
                    <TextBlock Grid.Column="1" Classes="meta" Text="{Binding LastSample, StringFormat='sampled {0}'}"/>
                </Grid>
                <TextBox Grid.Row="1" x:Name="LogBox" Text="{Binding Log}" IsReadOnly="True" AcceptsReturn="True"
                         TextWrapping="NoWrap" FontFamily="{DynamicResource MonoFont}" FontSize="11"
                         Background="Transparent" BorderThickness="0" Padding="14,4"/>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

(Empty-state-when-stopped is deferred: the console simply shows an empty mono area framed by the panel — already far better than the old void. A true empty-state overlay can be added later without rework.)

- [ ] **Step 3: Build + screenshot gate**

```bash
dotnet build ArkManager.slnx 2>&1 | tail -5
```
Expected: Build succeeded. Run harness, shoot Server tab, verify: status chip top-right, primary Start (ember) vs ghost Stop, 4 vitals tiles, framed console with header. Tweak spacing if cramped.

- [ ] **Step 4: Commit**

```bash
git add src/ArkManager.Desktop/ViewModels/ServerViewModel.cs src/ArkManager.Desktop/Views/ServerView.axaml
git commit -m "feat(ui): ServerView — Field Manual (vitals tiles, status chip, framed console)"
```

---

## Task 8: RconView

**Files:**
- Rewrite: `src/ArkManager.Desktop/Views/RconView.axaml`

- [ ] **Step 1: Rewrite RconView.axaml** (bindings: `Host`, `Port`, `Password`, `Connected`, `Lines`, `Command`, commands `Connect/Disconnect/Send/Saveworld/DoExit/CopyLog/Clear`)

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArkManager.App.ViewModels"
             x:Class="ArkManager.App.Views.RconView"
             x:DataType="vm:RconViewModel">
    <Grid RowDefinitions="auto,auto,*,auto" RowSpacing="16">
        <TextBlock Grid.Row="0" Classes="h1" Text="RCON"/>

        <Grid Grid.Row="1" ColumnDefinitions="*,130,*,auto,auto" ColumnSpacing="8">
            <TextBox Grid.Column="0" Text="{Binding Host}" PlaceholderText="host"/>
            <NumericUpDown Grid.Column="1" Value="{Binding Port}" Minimum="1" Maximum="65535"/>
            <TextBox Grid.Column="2" Text="{Binding Password}" PlaceholderText="admin password" PasswordChar="•"/>
            <Button Grid.Column="3" Classes="primary" Content="Connect" Command="{Binding ConnectCommand}" IsEnabled="{Binding !Connected}"/>
            <Button Grid.Column="4" Content="Disconnect" Command="{Binding DisconnectCommand}" IsEnabled="{Binding Connected}"/>
        </Grid>

        <Border Grid.Row="2" Classes="console">
            <Grid RowDefinitions="auto,*">
                <TextBlock Grid.Row="0" Classes="section" Text="OUTPUT" Margin="14,9"/>
                <TextBox Grid.Row="1" x:Name="LogBox" Text="{Binding Lines}" IsReadOnly="True" AcceptsReturn="True"
                         TextWrapping="NoWrap" FontFamily="{DynamicResource MonoFont}" FontSize="11"
                         Background="Transparent" BorderThickness="0" Padding="14,4"/>
            </Grid>
        </Border>

        <Grid Grid.Row="3" ColumnDefinitions="*,auto,auto,auto,auto,auto" ColumnSpacing="6">
            <TextBox Grid.Column="0" Text="{Binding Command}" PlaceholderText="RCON command (e.g. ListPlayers)"/>
            <Button Grid.Column="1" Classes="primary" Content="Send" Command="{Binding SendCommand}" IsEnabled="{Binding Connected}"/>
            <Button Grid.Column="2" Classes="chip" Content="saveworld" Command="{Binding SaveworldCommand}" IsEnabled="{Binding Connected}"/>
            <Button Grid.Column="3" Classes="chip" Content="DoExit" Command="{Binding DoExitCommand}" IsEnabled="{Binding Connected}"/>
            <Button Grid.Column="4" Classes="icon" Command="{Binding CopyLogCommand}" ToolTip.Tip="Copy log"><PathIcon Data="{DynamicResource IconCopy}"/></Button>
            <Button Grid.Column="5" Classes="icon" Command="{Binding ClearCommand}" ToolTip.Tip="Clear"><PathIcon Data="{DynamicResource IconTrash}"/></Button>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Build + screenshot gate**

Run: `dotnet build ArkManager.slnx 2>&1 | tail -5` → Build succeeded. Harness → RCON tab → verify framed output, primary Connect/Send, mono quick-command chips, icon buttons.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Desktop/Views/RconView.axaml
git commit -m "feat(ui): RconView — Field Manual"
```

---

## Task 9: ConfigView

**Files:**
- Rewrite: `src/ArkManager.Desktop/Views/ConfigView.axaml` (keep `ConfigView.axaml.cs` event handlers `OnSearchKeyDown`, `OnFindNext` — referenced by name; do not rename)

Bindings used: `SelectedTabIndex`, `KnownMaps`, `SelectedMap`, `Map`, `SessionName`, `MaxPlayers`, `Port`, `QueryPort`, `RconEnabled`, `RconPort`, `ServerPassword`, `AdminPassword`, `SpectatorPassword`, `NoBattlEye`, `AutoManagedMods`, `ClusterId`, `ClusterDirOverride`, `ExtraCommandLineArgs`, `ExtraQueryString`, `GameUserSettingsRaw`, `GameIniRaw`, `CommandLinePreview`, `SaveButtonText`, `SaveContextCommand`, `ReloadCommand`, `Status`.

- [ ] **Step 1: Rewrite ConfigView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArkManager.App.ViewModels"
             x:Class="ArkManager.App.Views.ConfigView"
             x:DataType="vm:ConfigViewModel">
    <Grid RowDefinitions="auto,*,auto" RowSpacing="14">
        <StackPanel Grid.Row="0" Spacing="6">
            <TextBlock Classes="h1" Text="Configuration"/>
            <TextBlock Classes="meta" Text="ShooterGame/Saved/Config/WindowsServer"/>
        </StackPanel>

        <TabControl Grid.Row="1" Classes="seg" SelectedIndex="{Binding SelectedTabIndex}" Padding="0">
            <TabItem Header="Basic">
                <ScrollViewer Padding="0">
                    <StackPanel Spacing="12" Margin="0,10,0,0">
                        <Border Classes="panel">
                            <StackPanel Spacing="2">
                                <TextBlock Classes="section" Text="SERVER" Margin="0,0,0,8"/>
                                <Grid ColumnDefinitions="170,*" RowDefinitions="auto,auto,auto" RowSpacing="10" ColumnSpacing="14">
                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Map" VerticalAlignment="Center"/>
                                    <Grid Grid.Row="0" Grid.Column="1" ColumnDefinitions="*,*" ColumnSpacing="8">
                                        <ComboBox Grid.Column="0" ItemsSource="{Binding KnownMaps}" SelectedItem="{Binding SelectedMap}" PlaceholderText="Preset…" HorizontalAlignment="Stretch"/>
                                        <TextBox Grid.Column="1" Classes="mono" Text="{Binding Map}" PlaceholderText="custom map"/>
                                    </Grid>
                                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Session name" VerticalAlignment="Center"/>
                                    <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding SessionName}"/>
                                    <TextBlock Grid.Row="2" Grid.Column="0" Text="Max players" VerticalAlignment="Center"/>
                                    <NumericUpDown Grid.Row="2" Grid.Column="1" Value="{Binding MaxPlayers}" Minimum="1" Maximum="200" HorizontalAlignment="Left" Width="140"/>
                                </Grid>
                            </StackPanel>
                        </Border>

                        <Border Classes="panel">
                            <StackPanel Spacing="2">
                                <TextBlock Classes="section" Text="NETWORK" Margin="0,0,0,8"/>
                                <Grid ColumnDefinitions="170,*" RowDefinitions="auto,auto,auto" RowSpacing="10" ColumnSpacing="14">
                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Game port" VerticalAlignment="Center"/>
                                    <NumericUpDown Grid.Row="0" Grid.Column="1" Value="{Binding Port}" Minimum="1024" Maximum="65535" HorizontalAlignment="Left" Width="140"/>
                                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Query port" VerticalAlignment="Center"/>
                                    <NumericUpDown Grid.Row="1" Grid.Column="1" Value="{Binding QueryPort}" Minimum="1024" Maximum="65535" HorizontalAlignment="Left" Width="140"/>
                                    <TextBlock Grid.Row="2" Grid.Column="0" Text="RCON" VerticalAlignment="Center"/>
                                    <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Horizontal" Spacing="14">
                                        <ToggleSwitch IsChecked="{Binding RconEnabled}" OnContent="Enabled" OffContent="Disabled"/>
                                        <NumericUpDown Value="{Binding RconPort}" Minimum="1024" Maximum="65535" Width="140"/>
                                    </StackPanel>
                                </Grid>
                            </StackPanel>
                        </Border>

                        <Border Classes="panel">
                            <StackPanel Spacing="2">
                                <TextBlock Classes="section" Text="PASSWORDS" Margin="0,0,0,8"/>
                                <Grid ColumnDefinitions="170,*" RowDefinitions="auto,auto,auto" RowSpacing="10" ColumnSpacing="14">
                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Server password" VerticalAlignment="Center"/>
                                    <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding ServerPassword}"/>
                                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Admin password" VerticalAlignment="Center"/>
                                    <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding AdminPassword}"/>
                                    <TextBlock Grid.Row="2" Grid.Column="0" Text="Spectator password" VerticalAlignment="Center"/>
                                    <TextBox Grid.Row="2" Grid.Column="1" Text="{Binding SpectatorPassword}"/>
                                </Grid>
                            </StackPanel>
                        </Border>

                        <Border Classes="panel">
                            <StackPanel Spacing="10">
                                <TextBlock Classes="section" Text="ADVANCED"/>
                                <CheckBox IsChecked="{Binding NoBattlEye}" Content="-NoBattlEye (required for Wine)"/>
                                <CheckBox IsChecked="{Binding AutoManagedMods}" Content="-automanagedmods (auto-download mods)"/>
                            </StackPanel>
                        </Border>

                        <Border Classes="panel">
                            <StackPanel Spacing="2">
                                <TextBlock Classes="section" Text="CLUSTER" Margin="0,0,0,8"/>
                                <Grid ColumnDefinitions="170,*" RowDefinitions="auto,auto" RowSpacing="10" ColumnSpacing="14">
                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Cluster ID" VerticalAlignment="Center"/>
                                    <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding ClusterId}" PlaceholderText="shared across cluster servers"/>
                                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Cluster dir override" VerticalAlignment="Center"/>
                                    <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding ClusterDirOverride}" PlaceholderText="optional"/>
                                </Grid>
                            </StackPanel>
                        </Border>

                        <Border Classes="panel">
                            <StackPanel Spacing="10">
                                <TextBlock Classes="section" Text="EXTRA ARGUMENTS"/>
                                <TextBlock Text="Extra CLI args" Foreground="{DynamicResource MutedBrush}"/>
                                <TextBox Classes="mono" Text="{Binding ExtraCommandLineArgs}"/>
                                <TextBlock Text="Extra ?query-string params (separate with '?')" Foreground="{DynamicResource MutedBrush}"/>
                                <TextBox Classes="mono" Text="{Binding ExtraQueryString}"/>
                            </StackPanel>
                        </Border>
                        <Border Height="40"/>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>

            <TabItem Header="GameUserSettings.ini">
                <Grid RowDefinitions="auto,*" Margin="0,10,0,0">
                    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="6" Margin="0,0,0,8">
                        <TextBox Width="240" PlaceholderText="Find…" KeyDown="OnSearchKeyDown"/>
                        <Button Content="Find next ⏎" Click="OnFindNext"/>
                    </StackPanel>
                    <Border Grid.Row="1" Classes="console">
                        <TextBox Text="{Binding GameUserSettingsRaw}" AcceptsReturn="True" TextWrapping="NoWrap"
                                 FontFamily="{DynamicResource MonoFont}" FontSize="12"
                                 Background="Transparent" BorderThickness="0" Padding="12"/>
                    </Border>
                </Grid>
            </TabItem>

            <TabItem Header="Game.ini">
                <Grid RowDefinitions="auto,*" Margin="0,10,0,0">
                    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="6" Margin="0,0,0,8">
                        <TextBox Width="240" PlaceholderText="Find…" KeyDown="OnSearchKeyDown"/>
                        <Button Content="Find next ⏎" Click="OnFindNext"/>
                    </StackPanel>
                    <Border Grid.Row="1" Classes="console">
                        <TextBox Text="{Binding GameIniRaw}" AcceptsReturn="True" TextWrapping="NoWrap"
                                 FontFamily="{DynamicResource MonoFont}" FontSize="12"
                                 Background="Transparent" BorderThickness="0" Padding="12"/>
                    </Border>
                </Grid>
            </TabItem>

            <TabItem Header="CLI Preview">
                <Border Classes="console" Margin="0,10,0,0">
                    <ScrollViewer>
                        <TextBlock Text="{Binding CommandLinePreview}" TextWrapping="Wrap"
                                   FontFamily="{DynamicResource MonoFont}" FontSize="12"
                                   Foreground="{DynamicResource TextBrush}" Margin="14"/>
                    </ScrollViewer>
                </Border>
            </TabItem>
        </TabControl>

        <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="10">
            <Button Classes="primary" Command="{Binding SaveContextCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <PathIcon Data="{DynamicResource IconSave}"/><TextBlock Text="{Binding SaveButtonText}"/>
                </StackPanel>
            </Button>
            <Button Command="{Binding ReloadCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <PathIcon Data="{DynamicResource IconRefresh}"/><TextBlock Text="Reload"/>
                </StackPanel>
            </Button>
            <TextBlock Text="{Binding Status}" VerticalAlignment="Center" Foreground="{DynamicResource MutedBrush}" Margin="6,0,0,0"/>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Build + screenshot gate** — `dotnet build ArkManager.slnx 2>&1 | tail -5`; harness → Config tab; verify segmented tabs, grouped panels with section headers, RCON ToggleSwitch, mono ini editors framed as console.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Desktop/Views/ConfigView.axaml
git commit -m "feat(ui): ConfigView — Field Manual (panels, segmented tabs, toggle)"
```

---

## Task 10: ModsView

**Files:**
- Rewrite: `src/ArkManager.Desktop/Views/ModsView.axaml` (bindings: `NewModId`, `AddCommand`, `Mods` of `ModEntry{Id,DisplayName}`, `Selected`, `MoveUp/MoveDown/Remove/OpenInCurseForge/ResolveNames/Reload`, `Status`)

- [ ] **Step 1: Rewrite ModsView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArkManager.App.ViewModels"
             xmlns:mods="using:ArkManager.Core.Services.Mods"
             x:Class="ArkManager.App.Views.ModsView"
             x:DataType="vm:ModsViewModel">
    <Grid RowDefinitions="auto,auto,*,auto" RowSpacing="14">
        <TextBlock Grid.Row="0" Classes="h1" Text="Mods"/>

        <Grid Grid.Row="1" ColumnDefinitions="*,auto" ColumnSpacing="8">
            <TextBox Grid.Column="0" Text="{Binding NewModId}" PlaceholderText="CurseForge ID (comma/space-separated for several)"/>
            <Button Grid.Column="1" Classes="primary" Command="{Binding AddCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconPlus}"/><TextBlock Text="Add"/></StackPanel>
            </Button>
        </Grid>

        <Border Grid.Row="2" Classes="console" Padding="8">
            <ListBox Classes="rows" ItemsSource="{Binding Mods}" SelectedItem="{Binding Selected}" Background="Transparent">
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="mods:ModEntry">
                        <Border Classes="tile">
                            <StackPanel Orientation="Horizontal" Spacing="14">
                                <TextBlock Text="{Binding Id}" FontFamily="{DynamicResource MonoFont}" FontWeight="Medium"
                                           Foreground="{DynamicResource AccentInkBrush}" Width="100" VerticalAlignment="Center"/>
                                <TextBlock Text="{Binding DisplayName}" Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center"/>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>

        <StackPanel Grid.Row="3" Orientation="Horizontal" Spacing="8">
            <Button Classes="icon" Command="{Binding MoveUpCommand}" ToolTip.Tip="Move up"><PathIcon Data="{DynamicResource IconArrowUp}"/></Button>
            <Button Classes="icon" Command="{Binding MoveDownCommand}" ToolTip.Tip="Move down"><PathIcon Data="{DynamicResource IconArrowDown}"/></Button>
            <Button Classes="danger" Command="{Binding RemoveCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconTrash}"/><TextBlock Text="Remove"/></StackPanel>
            </Button>
            <Button Command="{Binding OpenInCurseForgeCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconGlobe}"/><TextBlock Text="CurseForge"/></StackPanel>
            </Button>
            <Button Command="{Binding ResolveNamesCommand}" Content="Resolve names"/>
            <Button Command="{Binding ReloadCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconRefresh}"/><TextBlock Text="Reload"/></StackPanel>
            </Button>
            <TextBlock Text="{Binding Status}" VerticalAlignment="Center" Foreground="{DynamicResource MutedBrush}" Margin="6,0,0,0"/>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Build + screenshot gate** — verify list rows as tiles, icon buttons, danger Remove.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Desktop/Views/ModsView.axaml
git commit -m "feat(ui): ModsView — Field Manual"
```

---

## Task 11: BackupsView (+ VM English copy + display members)

**Files:**
- Modify: `src/ArkManager.Core/Services/Backups/BackupService.cs` (add display members to `BackupInfo`)
- Modify: `src/ArkManager.Desktop/ViewModels/BackupsViewModel.cs` (English copy + `Summary`)
- Rewrite: `src/ArkManager.Desktop/Views/BackupsView.axaml`

- [ ] **Step 1: Add display members to BackupInfo**

`BackupInfo` is `public sealed record BackupInfo(string FilePath, DateTime CreatedUtc, long SizeBytes, string? Note);`. Add computed display members (pure, use Task 1 helpers):

```csharp
using ArkManager.Core.Util;
// ...
public sealed record BackupInfo(string FilePath, DateTime CreatedUtc, long SizeBytes, string? Note)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Note) ? "Auto snapshot" : Note!;
    public string Age => DisplayFormat.RelativeTime(CreatedUtc, DateTime.UtcNow);
    public string SizeText => DisplayFormat.HumanSize(SizeBytes);
}
```

(Computed props don't notify, which is fine: the list is rebuilt on every `Reload()`.)

- [ ] **Step 2: English copy + Summary in BackupsViewModel**

Add `Summary` property and translate the user-facing strings. Apply these exact replacements in `BackupsViewModel.cs`:

```csharp
    [ObservableProperty] private string _autoBackupStatus = "Auto-backup off";
    [ObservableProperty] private string _summary = "";
```

In `UpdateAutoStatus()`:

```csharp
            AutoBackupStatus = left <= TimeSpan.Zero
                ? "Auto-backup: running…"
                : $"Auto-backup in {(int)left.TotalMinutes:00}:{left.Seconds:00}";
        }
        else AutoBackupStatus = "Auto-backup off";
```

In `Reload()` replace the status line and set the summary:

```csharp
        foreach (var b in _service.ListBackups()) Backups.Add(b);
        var total = 0L;
        foreach (var b in Backups) total += b.SizeBytes;
        Summary = $"{Backups.Count} snapshots · {DisplayFormat.HumanSize(total)} total";
        Status = "";
```

In `CreateAsync()`: `Status = "Created: " + Path.GetFileName(info.FilePath);` and catch `Status = "Error: " + ex.Message;`
In `RestoreAsync()`: `Status = "Restored from: " + Path.GetFileName(Selected.FilePath);` and catch `Status = "Error: " + ex.Message;`
In `Delete()` catch: `Status = "Error: " + ex.Message;`

Add `using ArkManager.Core.Util;` at top.

- [ ] **Step 3: Rewrite BackupsView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArkManager.App.ViewModels"
             xmlns:bk="using:ArkManager.Core.Services.Backups"
             x:Class="ArkManager.App.Views.BackupsView"
             x:DataType="vm:BackupsViewModel">
    <Grid RowDefinitions="auto,auto,*,auto" RowSpacing="14">
        <Grid Grid.Row="0" ColumnDefinitions="*,auto">
            <StackPanel Grid.Column="0" Spacing="6">
                <TextBlock Classes="h1" Text="Backups"/>
                <TextBlock Classes="meta" Text="{Binding Summary}"/>
            </StackPanel>
            <Border Grid.Column="1" Classes="pill" VerticalAlignment="Center">
                <StackPanel Orientation="Horizontal" Spacing="7">
                    <PathIcon Data="{DynamicResource IconClock}" Width="13" Height="13" Foreground="{DynamicResource AccentInkBrush}"/>
                    <TextBlock Text="{Binding AutoBackupStatus}" FontSize="12" FontWeight="SemiBold" Foreground="{DynamicResource AccentInkBrush}"/>
                </StackPanel>
            </Border>
        </Grid>

        <Grid Grid.Row="1" ColumnDefinitions="*,auto" ColumnSpacing="8">
            <TextBox Grid.Column="0" Text="{Binding Note}" PlaceholderText="Optional note for this snapshot…"/>
            <Button Grid.Column="1" Classes="primary" Command="{Binding CreateCommand}" IsEnabled="{Binding !Busy}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconPlus}"/><TextBlock Text="Create backup"/></StackPanel>
            </Button>
        </Grid>

        <Border Grid.Row="2" Classes="console" Padding="8">
            <ListBox Classes="rows" ItemsSource="{Binding Backups}" SelectedItem="{Binding Selected}" Background="Transparent">
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="bk:BackupInfo">
                        <Border Classes="tile">
                            <Grid ColumnDefinitions="34,*,auto">
                                <PathIcon Grid.Column="0" Data="{DynamicResource IconBackups}" Width="18" Height="18"
                                          Foreground="{DynamicResource AccentInkBrush}" VerticalAlignment="Center"/>
                                <StackPanel Grid.Column="1" Spacing="3" VerticalAlignment="Center">
                                    <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}"/>
                                    <TextBlock Classes="meta" Text="{Binding Age}"/>
                                </StackPanel>
                                <TextBlock Grid.Column="2" Classes="meta" Text="{Binding SizeText}" VerticalAlignment="Center"/>
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>

        <StackPanel Grid.Row="3" Orientation="Horizontal" Spacing="8">
            <Button Classes="primary" Command="{Binding RestoreCommand}" IsEnabled="{Binding !Busy}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconRestore}"/><TextBlock Text="Restore (clean)"/></StackPanel>
            </Button>
            <Button Classes="danger" Command="{Binding DeleteCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconTrash}"/><TextBlock Text="Delete"/></StackPanel>
            </Button>
            <Button Command="{Binding OpenFolderCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconFolder}"/><TextBlock Text="Show in Finder"/></StackPanel>
            </Button>
            <Button Command="{Binding ReloadCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconRefresh}"/><TextBlock Text="Reload"/></StackPanel>
            </Button>
            <ProgressBar Width="160" Minimum="0" Maximum="1" Value="{Binding Progress}" IsVisible="{Binding Busy}" Height="6" VerticalAlignment="Center"/>
            <TextBlock Text="{Binding Status}" VerticalAlignment="Center" Foreground="{DynamicResource MutedBrush}" Margin="6,0,0,0"/>
        </StackPanel>
    </Grid>
</UserControl>
```

Note `IconRestore` is not in Icons.axaml — add it there (alias of refresh): `<StreamGeometry x:Key="IconRestore">M12 5 A7 7 0 1 1 5 12 H7 A5 5 0 1 0 12 7 V10 L8 6 L12 2 Z</StreamGeometry>` (or reuse `IconRefresh` in the XAML instead). Pick one and keep consistent.

- [ ] **Step 4: Build + test + screenshot gate**

```bash
dotnet build ArkManager.slnx 2>&1 | tail -5
dotnet test ArkManager.slnx --filter DisplayFormatTests
```
Expected: build OK, tests pass. Harness → Backups tab → verify human-readable rows (name + relative time + size), countdown pill, no raw paths.

- [ ] **Step 5: Commit**

```bash
git add src/ArkManager.Core/Services/Backups/BackupService.cs \
        src/ArkManager.Desktop/ViewModels/BackupsViewModel.cs \
        src/ArkManager.Desktop/Views/BackupsView.axaml \
        src/ArkManager.Desktop/Themes/Icons.axaml
git commit -m "feat(ui): BackupsView — human-readable rows + English copy"
```

---

## Task 12: DoctorView

**Files:**
- Rewrite: `src/ArkManager.Desktop/Views/DoctorView.axaml` (bindings: `RunCommand`, `InstallWineCommand`, `Busy`, `Summary`, `Results` of `CheckResult{Ok,Name,Detail,FixHint}`, `InstallLog`, `CopyInstallLogCommand`; converter `OkIcon`)

The empty right-hand log void is removed: the install log shows below the checks, only when non-empty (`IsVisible` on `InstallLog`).

- [ ] **Step 1: Rewrite DoctorView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArkManager.App.ViewModels"
             xmlns:doc="using:ArkManager.Core.Services.Doctor"
             xmlns:cv="using:ArkManager.App.Converters"
             x:Class="ArkManager.App.Views.DoctorView"
             x:DataType="vm:DoctorViewModel">
    <Grid RowDefinitions="auto,auto,*,auto" RowSpacing="14">
        <TextBlock Grid.Row="0" Classes="h1" Text="Doctor"/>

        <Grid Grid.Row="1" ColumnDefinitions="auto,auto,*">
            <Button Grid.Column="0" Classes="primary" Command="{Binding RunCommand}" IsEnabled="{Binding !Busy}" Margin="0,0,8,0">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconRefresh}"/><TextBlock Text="Run checks"/></StackPanel>
            </Button>
            <Button Grid.Column="1" Command="{Binding InstallWineCommand}" IsEnabled="{Binding !Busy}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconInstall}"/><TextBlock Text="Install Wine (brew)"/></StackPanel>
            </Button>
            <TextBlock Grid.Column="2" Text="{Binding Summary}" VerticalAlignment="Center" HorizontalAlignment="Right"
                       Foreground="{DynamicResource MutedBrush}"/>
        </Grid>

        <ScrollViewer Grid.Row="2">
            <ItemsControl ItemsSource="{Binding Results}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="doc:CheckResult">
                        <Border Classes="tile" Margin="0,0,0,8">
                            <Grid ColumnDefinitions="28,*,*" ColumnSpacing="10">
                                <TextBlock Grid.Column="0" Text="{Binding Ok, Converter={x:Static cv:OkIcon.Instance}}" FontSize="15" VerticalAlignment="Center"/>
                                <StackPanel Grid.Column="1" Spacing="3" VerticalAlignment="Center">
                                    <TextBlock Text="{Binding Name}" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}"/>
                                    <TextBlock Classes="meta" Text="{Binding Detail}" TextWrapping="Wrap"/>
                                </StackPanel>
                                <TextBlock Grid.Column="2" Classes="meta" Text="{Binding FixHint}" TextWrapping="Wrap" VerticalAlignment="Center"/>
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>

        <Border Grid.Row="3" Classes="console" IsVisible="{Binding InstallLog, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" Height="160">
            <Grid RowDefinitions="auto,*">
                <Grid Grid.Row="0" ColumnDefinitions="*,auto" Margin="14,9">
                    <TextBlock Grid.Column="0" Classes="section" Text="INSTALL LOG" VerticalAlignment="Center"/>
                    <Button Grid.Column="1" Classes="icon" Command="{Binding CopyInstallLogCommand}" ToolTip.Tip="Copy"><PathIcon Data="{DynamicResource IconCopy}"/></Button>
                </Grid>
                <TextBox Grid.Row="1" x:Name="LogBox" Text="{Binding InstallLog}" IsReadOnly="True" AcceptsReturn="True"
                         TextWrapping="NoWrap" FontFamily="{DynamicResource MonoFont}" FontSize="11"
                         Background="Transparent" BorderThickness="0" Padding="14,2"/>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

(The `ProgressBar` for `Busy` was dropped from the bottom; if desired, the Run button being disabled already signals busy. Keep it simple.)

- [ ] **Step 2: Build + screenshot gate** — verify check rows as tiles, install log hidden when empty.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Desktop/Views/DoctorView.axaml
git commit -m "feat(ui): DoctorView — Field Manual (check tiles, collapsible log)"
```

---

## Task 13: InstallView

**Files:**
- Rewrite: `src/ArkManager.Desktop/Views/InstallView.axaml` (bindings: `SteamCmdState`, `InstallSteamCmdCommand`, `Busy`, `ServerInstallPath`, `BrowseServerFolderCommand`, `InstallOrUpdateServerCommand`, `OpenServerFolderCommand`, `InstalledBuild`, `InstalledAt`, `LatestBuild`, `UpdateStatus`, `CheckForUpdatesCommand`, `Log`, `CopyLogCommand`, `ClearLogCommand`)

- [ ] **Step 1: Rewrite InstallView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArkManager.App.ViewModels"
             x:Class="ArkManager.App.Views.InstallView"
             x:DataType="vm:InstallViewModel">
    <Grid RowDefinitions="auto,auto,*" RowSpacing="14">
        <TextBlock Grid.Row="0" Classes="h1" Text="Install"/>

        <StackPanel Grid.Row="1" Spacing="12">
            <Border Classes="panel">
                <StackPanel Spacing="10">
                    <TextBlock Classes="section" Text="STEAMCMD"/>
                    <TextBlock Text="{Binding SteamCmdState}" Foreground="{DynamicResource TextBrush}"/>
                    <Button Classes="primary" Content="Install / Reinstall SteamCMD" Command="{Binding InstallSteamCmdCommand}" IsEnabled="{Binding !Busy}" HorizontalAlignment="Left"/>
                </StackPanel>
            </Border>

            <Border Classes="panel">
                <StackPanel Spacing="10">
                    <TextBlock Classes="section" Text="ASA DEDICATED SERVER · APP 2430930"/>
                    <TextBlock Text="Install path" Foreground="{DynamicResource MutedBrush}"/>
                    <Grid ColumnDefinitions="*,auto" ColumnSpacing="8">
                        <TextBox Grid.Column="0" Classes="mono" Text="{Binding ServerInstallPath}"/>
                        <Button Grid.Column="1" Classes="icon" Command="{Binding BrowseServerFolderCommand}" ToolTip.Tip="Browse"><PathIcon Data="{DynamicResource IconFolder}"/></Button>
                    </Grid>
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button Classes="primary" Content="Install / Update server" Command="{Binding InstallOrUpdateServerCommand}" IsEnabled="{Binding !Busy}"/>
                        <Button Command="{Binding OpenServerFolderCommand}">
                            <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconFolder}"/><TextBlock Text="Show in Finder"/></StackPanel>
                        </Button>
                    </StackPanel>
                    <ProgressBar IsIndeterminate="True" IsVisible="{Binding Busy}" Height="4"/>
                </StackPanel>
            </Border>

            <Border Classes="panel">
                <StackPanel Spacing="10">
                    <TextBlock Classes="section" Text="SERVER VERSION"/>
                    <Grid ColumnDefinitions="auto,*" RowDefinitions="auto,auto,auto" RowSpacing="6" ColumnSpacing="14">
                        <TextBlock Grid.Row="0" Grid.Column="0" Text="Installed build" Foreground="{DynamicResource MutedBrush}"/>
                        <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding InstalledBuild}" FontFamily="{DynamicResource MonoFont}"/>
                        <TextBlock Grid.Row="1" Grid.Column="0" Text="Last updated" Foreground="{DynamicResource MutedBrush}"/>
                        <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding InstalledAt}"/>
                        <TextBlock Grid.Row="2" Grid.Column="0" Text="Latest build" Foreground="{DynamicResource MutedBrush}"/>
                        <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding LatestBuild}" FontFamily="{DynamicResource MonoFont}"/>
                    </Grid>
                    <TextBlock Text="{Binding UpdateStatus}" FontWeight="SemiBold" Foreground="{DynamicResource TextBrush}"/>
                    <Button Command="{Binding CheckForUpdatesCommand}" IsEnabled="{Binding !Busy}" HorizontalAlignment="Left">
                        <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconRefresh}"/><TextBlock Text="Check for updates"/></StackPanel>
                    </Button>
                </StackPanel>
            </Border>
        </StackPanel>

        <Border Grid.Row="2" Classes="console">
            <Grid RowDefinitions="auto,*">
                <Grid Grid.Row="0" ColumnDefinitions="*,auto,auto" Margin="14,9">
                    <TextBlock Grid.Column="0" Classes="section" Text="LOG" VerticalAlignment="Center"/>
                    <Button Grid.Column="1" Classes="icon" Command="{Binding CopyLogCommand}" ToolTip.Tip="Copy" Margin="0,0,6,0"><PathIcon Data="{DynamicResource IconCopy}"/></Button>
                    <Button Grid.Column="2" Classes="icon" Command="{Binding ClearLogCommand}" ToolTip.Tip="Clear"><PathIcon Data="{DynamicResource IconTrash}"/></Button>
                </Grid>
                <TextBox Grid.Row="1" x:Name="LogBox" Text="{Binding Log}" IsReadOnly="True" AcceptsReturn="True"
                         TextWrapping="NoWrap" FontFamily="{DynamicResource MonoFont}" FontSize="11"
                         Background="Transparent" BorderThickness="0" Padding="14,2"/>
            </Grid>
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Build + screenshot gate** — verify panels, framed log, English copy.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Desktop/Views/InstallView.axaml
git commit -m "feat(ui): InstallView — Field Manual + English copy"
```

---

## Task 14: SettingsView

**Files:**
- Rewrite: `src/ArkManager.Desktop/Views/SettingsView.axaml` (bindings: `WineBinaryPath`, `BrowseWineBinaryCommand`, `WinePrefixPath`, `BrowseWinePrefixCommand`, `ServerInstallPath`, `BrowseServerInstallCommand`, `BackupsDirectory`, `BrowseBackupsCommand`, `SteamCmdPath`, `BrowseSteamCmdCommand`, `CurseForgeApiKey`, `BackupRotationKeep`, `AutoRestartOnCrash`, `AutoRestartDelaySeconds`, `ScheduledRestartHours`, `AutoBackupIntervalMinutes`, `AutoBackupOnlyWhenRunning`, `DataDir`, `OpenDataFolderCommand`, `SaveCommand`, `Status`)

- [ ] **Step 1: Rewrite SettingsView.axaml**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ArkManager.App.ViewModels"
             x:Class="ArkManager.App.Views.SettingsView"
             x:DataType="vm:SettingsViewModel">
    <Grid RowDefinitions="auto,*,auto" RowSpacing="14">
        <TextBlock Grid.Row="0" Classes="h1" Text="Settings"/>

        <ScrollViewer Grid.Row="1">
            <StackPanel Spacing="12">
                <Border Classes="panel">
                    <StackPanel Spacing="10">
                        <TextBlock Classes="section" Text="WINE"/>
                        <TextBlock Text="wine64 binary (optional — empty = auto-detect)" Foreground="{DynamicResource MutedBrush}"/>
                        <Grid ColumnDefinitions="*,auto" ColumnSpacing="8">
                            <TextBox Grid.Column="0" Classes="mono" Text="{Binding WineBinaryPath}"/>
                            <Button Grid.Column="1" Classes="icon" Command="{Binding BrowseWineBinaryCommand}"><PathIcon Data="{DynamicResource IconFolder}"/></Button>
                        </Grid>
                        <TextBlock Text="WINEPREFIX (directory where wine keeps C:\)" Foreground="{DynamicResource MutedBrush}"/>
                        <Grid ColumnDefinitions="*,auto" ColumnSpacing="8">
                            <TextBox Grid.Column="0" Classes="mono" Text="{Binding WinePrefixPath}"/>
                            <Button Grid.Column="1" Classes="icon" Command="{Binding BrowseWinePrefixCommand}"><PathIcon Data="{DynamicResource IconFolder}"/></Button>
                        </Grid>
                    </StackPanel>
                </Border>

                <Border Classes="panel">
                    <StackPanel Spacing="10">
                        <TextBlock Classes="section" Text="PATHS"/>
                        <TextBlock Text="Server install path" Foreground="{DynamicResource MutedBrush}"/>
                        <Grid ColumnDefinitions="*,auto" ColumnSpacing="8">
                            <TextBox Grid.Column="0" Classes="mono" Text="{Binding ServerInstallPath}"/>
                            <Button Grid.Column="1" Classes="icon" Command="{Binding BrowseServerInstallCommand}"><PathIcon Data="{DynamicResource IconFolder}"/></Button>
                        </Grid>
                        <TextBlock Text="Backups directory" Foreground="{DynamicResource MutedBrush}"/>
                        <Grid ColumnDefinitions="*,auto" ColumnSpacing="8">
                            <TextBox Grid.Column="0" Classes="mono" Text="{Binding BackupsDirectory}"/>
                            <Button Grid.Column="1" Classes="icon" Command="{Binding BrowseBackupsCommand}"><PathIcon Data="{DynamicResource IconFolder}"/></Button>
                        </Grid>
                        <TextBlock Text="SteamCMD path (optional)" Foreground="{DynamicResource MutedBrush}"/>
                        <Grid ColumnDefinitions="*,auto" ColumnSpacing="8">
                            <TextBox Grid.Column="0" Classes="mono" Text="{Binding SteamCmdPath}"/>
                            <Button Grid.Column="1" Classes="icon" Command="{Binding BrowseSteamCmdCommand}"><PathIcon Data="{DynamicResource IconFolder}"/></Button>
                        </Grid>
                        <TextBlock Text="CurseForge API key (for resolving mod names)" Foreground="{DynamicResource MutedBrush}"/>
                        <TextBox Text="{Binding CurseForgeApiKey}" PasswordChar="•"/>
                        <Grid ColumnDefinitions="*,160" ColumnSpacing="12">
                            <TextBlock Grid.Column="0" Text="Backups to keep (0 = no rotation)" VerticalAlignment="Center"/>
                            <NumericUpDown Grid.Column="1" Value="{Binding BackupRotationKeep}" Minimum="0" Maximum="100"/>
                        </Grid>
                    </StackPanel>
                </Border>

                <Border Classes="panel">
                    <StackPanel Spacing="10">
                        <TextBlock Classes="section" Text="AUTO-MANAGEMENT"/>
                        <CheckBox IsChecked="{Binding AutoRestartOnCrash}" Content="Restart on non-zero exit / crash"/>
                        <Grid ColumnDefinitions="*,160" ColumnSpacing="12">
                            <TextBlock Grid.Column="0" Text="Delay before auto-restart (sec)" VerticalAlignment="Center"/>
                            <NumericUpDown Grid.Column="1" Value="{Binding AutoRestartDelaySeconds}" Minimum="1" Maximum="600"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,160" ColumnSpacing="12">
                            <TextBlock Grid.Column="0" Text="Scheduled restart every N hours (0 = off)" VerticalAlignment="Center"/>
                            <NumericUpDown Grid.Column="1" Value="{Binding ScheduledRestartHours}" Minimum="0" Maximum="168"/>
                        </Grid>
                        <Grid ColumnDefinitions="*,160" ColumnSpacing="12">
                            <TextBlock Grid.Column="0" Text="Auto-backup every N minutes (0 = off)" VerticalAlignment="Center"/>
                            <NumericUpDown Grid.Column="1" Value="{Binding AutoBackupIntervalMinutes}" Minimum="0" Maximum="1440"/>
                        </Grid>
                        <CheckBox IsChecked="{Binding AutoBackupOnlyWhenRunning}" Content="Only auto-backup while the server is running"/>
                    </StackPanel>
                </Border>

                <Border Classes="panel">
                    <StackPanel Spacing="8">
                        <TextBlock Classes="section" Text="APP DATA"/>
                        <TextBlock Text="{Binding DataDir}" FontFamily="{DynamicResource MonoFont}" Foreground="{DynamicResource MutedBrush}"/>
                        <Button Command="{Binding OpenDataFolderCommand}" HorizontalAlignment="Left">
                            <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconFolder}"/><TextBlock Text="Show in Finder"/></StackPanel>
                        </Button>
                    </StackPanel>
                </Border>
                <Border Height="40"/>
            </StackPanel>
        </ScrollViewer>

        <StackPanel Grid.Row="2" Orientation="Horizontal" Spacing="10">
            <Button Classes="primary" Command="{Binding SaveCommand}">
                <StackPanel Orientation="Horizontal" Spacing="8"><PathIcon Data="{DynamicResource IconSave}"/><TextBlock Text="Save"/></StackPanel>
            </Button>
            <TextBlock Text="{Binding Status}" VerticalAlignment="Center" Foreground="{DynamicResource MutedBrush}"/>
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Build + screenshot gate** — verify panels, browse icon-buttons, English copy.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Desktop/Views/SettingsView.axaml
git commit -m "feat(ui): SettingsView — Field Manual + English copy"
```

---

## Task 15: Final verification pass

**Files:** none (verification only)

- [ ] **Step 1: Clean build + full tests**

```bash
dotnet build ArkManager.slnx 2>&1 | tail -8
dotnet test  ArkManager.slnx 2>&1 | tail -15
```
Expected: 0 build errors/warnings; all tests pass.

- [ ] **Step 2: Full screenshot sweep**

Run the harness (Appendix A) to capture all 8 tabs into `/tmp/ark-shots/redesign-*`. Review each:
- No emoji anywhere; all icons are solid glyphs.
- No raw filesystem paths in Backups.
- No black voids — every log/list is a framed console/panel.
- One language (English) on every screen.
- Primary actions are ember; destructive are red-tinted.

- [ ] **Step 3: Update CLAUDE.md UI notes**

Add a short note under "Подводные камни кода" documenting the new theme layer (Themes/*.axaml, token brushes, text/control style classes, embedded fonts) so future work uses tokens instead of hex. One paragraph.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: note Field Manual theme layer in CLAUDE.md"
```

---

## Appendix A: Screenshot harness

The app launches as GUI process `ArkManager.App`; nav is keyboard-navigable (the sidebar ListBox has focus on launch). This harness was validated during design. Screen Recording permission for the terminal is required.

```bash
# launch (run once, in background)
dotnet run --project src/ArkManager.Desktop/ArkManager.App.csproj -c Debug   # run_in_background

# capture helper
snap () {
  osascript -e 'tell application "System Events" to tell process "ArkManager.App" to set frontmost to true' 2>/dev/null
  /bin/sleep 0.5
  local b=$(osascript -e 'tell application "System Events" to tell process "ArkManager.App" to get {position, size} of window 1' 2>/dev/null | tr -d ' ')
  local x=$(echo $b|cut -d, -f1) y=$(echo $b|cut -d, -f2) w=$(echo $b|cut -d, -f3) h=$(echo $b|cut -d, -f4)
  screencapture -x -R"${x},${y},${w},${h}" "$1"
}
down () { osascript -e 'tell application "System Events" to tell process "ArkManager.App"' -e 'set frontmost to true' -e 'delay 0.3' -e 'key code 125' -e 'end tell' 2>/dev/null; /bin/sleep 0.4; }

mkdir -p /tmp/ark-shots
# home to top, then sweep
osascript -e 'tell application "System Events" to tell process "ArkManager.App"' -e 'set frontmost to true' -e 'repeat 10 times' -e 'key code 126' -e 'end repeat' -e 'end tell' 2>/dev/null
snap /tmp/ark-shots/redesign-01-server.png
down; snap /tmp/ark-shots/redesign-02-rcon.png
down; snap /tmp/ark-shots/redesign-03-install.png
down; snap /tmp/ark-shots/redesign-04-config.png
down; snap /tmp/ark-shots/redesign-05-mods.png
down; snap /tmp/ark-shots/redesign-06-backups.png
down; snap /tmp/ark-shots/redesign-07-doctor.png
down; snap /tmp/ark-shots/redesign-08-settings.png
```

Then `Read` each PNG to review.

## Self-review notes (addressed)

- **Spec coverage:** tokens (T3), typography+fonts (T2,T4), icons (T5), theme architecture/wiring (T6), components (T6 Controls), all 8 screens (T6–T14), English copy + glossary (per-screen + T11), pure helpers + tests (T1), verification incl. screenshot sweep (T15). All spec sections map to tasks.
- **Out-of-scope honored:** no packaging/onboarding/cross-platform/i18n; Doctor & Install restyled only.
- **Type consistency:** `BackupInfo.DisplayName/Age/SizeText`, `DisplayFormat.HumanSize/RelativeTime`, `NavItem(string,Geometry,ViewModelBase)`, `ServerViewModel.Identity` used consistently across tasks.
- **Known fragile points flagged:** hand-authored geometries (T5/T6 gate), `/template/` part-name selectors (T6 note), Google Fonts URL drift (T2), `AppSettings.Map` name check (T7).
