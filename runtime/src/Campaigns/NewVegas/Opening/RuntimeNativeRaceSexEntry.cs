using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeRaceSexEntry : CanvasLayer
{
    private FalloutPluginStack _records = null!;
    private FalloutNativeCharacterCreation _creation = null!;
    private FalloutInstallationSettings _settings = null!;
    private NativeOwnedRenderedDevice _device = null!;
    private NativeOwnedRenderedScreen _screen = null!;
    private NativeOwnedActorPreview? _portrait;
    private NativeOwnedMessageMenu? _confirmation;
    private SceneTree? _pausedTree;
    private bool _previousPause;
    private IDisposable? _background;
    private int _page, _previewRevision = -1;
    internal string? Error { get; private set; }
    internal event Action<FalloutNativeRaceSexSelection>? Accepted;
    internal event Action<Exception>? Failed;
    internal FalloutNativeCharacterCreation Creation => _creation;
    internal NativeOwnedActorPreview Portrait => _portrait ?? throw new InvalidOperationException("Character portrait is not ready.");
    internal ClassicPortraitMode PortraitMode { get; private set; }
    private Button? _envision;
    private CanvasLayer? _touchDebugLayer;
    private Button? _touchDebugToggle;
    private PanelContainer? _touchDebugPanel;
    private TextEdit? _touchDebugFeed;
    private readonly Queue<string> _touchDebugEvents = new();
    private int _touchDebugSequence;

    internal void EnableEnvision(ClassicPortraitMode initial)
    {
        if (_envision is not null) throw new InvalidOperationException("Envision is already configured.");
        _envision = new Button
        {
            Name = "Envision",
            CustomMinimumSize = new(270, 42),
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = "Show the same character as an illustrated Fallout portrait or a live 3D face.",
        };
        _envision.AddThemeFontSizeOverride("font_size", 17);
        _envision.AddThemeColorOverride("font_color", new Color("94f58d"));
        _envision.Pressed += () => SetPortraitMode(PortraitMode == ClassicPortraitMode.Illustrated
            ? ClassicPortraitMode.Live3D : ClassicPortraitMode.Illustrated);
        AddChild(_envision); _screen.RegisterExternalControl(_envision);
        _envision.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _envision.Position = new(20, GetViewport().GetVisibleRect().Size.Y - 58);
        SetPortraitMode(initial);
    }

    internal void AddAppearanceBackButton(Action close)
    {
        var back = new Button { Name = "AppearanceBack", Text = "Back to world preview", CustomMinimumSize = new(230, 42) };
        AddChild(back); _screen.RegisterExternalControl(back);
        back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        back.Position = GetViewport().GetVisibleRect().Size - new Vector2(250, 58);
        back.Pressed += close;
    }

    internal void SetPortraitMode(ClassicPortraitMode mode)
    {
        _screen.SetPortraitMode(mode); PortraitMode = mode;
        _portrait?.SetIllustratedBackdrop(mode == ClassicPortraitMode.Illustrated);
        if (_envision is not null)
        {
            _envision.Text = mode == ClassicPortraitMode.Illustrated ? "ENVISION · ILLUSTRATED" : "ENVISION · LIVE 3D";
            _envision.SetPressedNoSignal(mode == ClassicPortraitMode.Illustrated);
        }
    }
    internal void SelectPage(int page)
    {
        if (page < 0 || page >= _creation.Headers.Count) throw new ArgumentOutOfRangeException(nameof(page));
        _page = page; ShowPage();
    }
    internal object State => new
    {
        page = _page,
        revision = _creation.Revision,
        previewRevision = _previewRevision,
        character = _creation.Selection,
        error = Error,
        sourceControls = _creation.Controls.Controls.Count,
        parity = "unverified"
    };

    internal void Configure(FalloutNativeRaceSexContract contract, FalloutNativeRaceSexSelection current, FalloutPluginStack records,
        RuntimeNativeImageSpace? imageSpace = null)
    {
        Name = "NativeRaceSexEntry"; Layer = 120; ProcessMode = ProcessModeEnum.Always;
        _records = records;
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Creation has no owned content source.");
        _settings = FalloutInstallationSettings.Read(source);
        _creation = new(records, contract, current, _settings);
        _device = new("meshes/terminals/nv_reflectron_ui.nif", _settings) { Size = GetViewport().GetVisibleRect().Size };
        AddChild(_device);
        _screen = new(_device, records, _settings, Fail); AddChild(_screen);
        _screen.Menu.Navigate += Navigate;
        _screen.TouchDiagnostic += RecordTouchDiagnostic;
        BuildTouchDebugOverlay();
        GetViewport().SizeChanged += Resize;
        _pausedTree = GetTree(); _previousPause = _pausedTree.Paused; _pausedTree.Paused = true;
        _background = imageSpace?.BeginMenuBackground(records, FalloutMenuBackgroundKind.Popup);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        RefreshPreview(); ShowPage();
        GD.Print($"OPENNV_NATIVE_CREATION_PRESENTED pages={_creation.Headers.Count} controls={_creation.Controls.Controls.Count} source=owned-nif-xml-fnt-ctl-player-records parity=unverified");
    }
    private void Resize()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _device.Size = size;
        if (_touchDebugToggle is not null) _touchDebugToggle.Position = new Vector2(Mathf.Max(8, size.X - 190), 8);
        if (_touchDebugPanel is not null) _touchDebugPanel.Size = new Vector2(Mathf.Max(280, size.X - 24), Mathf.Clamp(size.Y * 0.55f, 300, 560));
    }

    private void BuildTouchDebugOverlay()
    {
        // This is the actual Race/Sex entry shown on the user's device.
        // A child CanvasLayer above the device and portrait guarantees overlay ordering.
        var layer = new CanvasLayer { Name = "RaceSexTouchDebugRoot", Layer = 125 };
        AddChild(layer);
        _touchDebugLayer = layer;
        var size = GetViewport().GetVisibleRect().Size;
        var toggle = new Button { Name = "RaceSexTouchDebugToggle", Text = "TOUCH DEBUG",
            Position = new Vector2(Mathf.Max(8, size.X - 190), 8), Size = new Vector2(176, 44),
            ZIndex = 4096, MouseFilter = Control.MouseFilterEnum.Stop, FocusMode = Control.FocusModeEnum.All };
        toggle.Pressed += () =>
        {
            if (_touchDebugPanel is null) return;
            _touchDebugPanel.Visible = !_touchDebugPanel.Visible;
            RecordTouchDiagnostic("overlay " + (_touchDebugPanel.Visible ? "opened" : "closed"));
        };
        layer.AddChild(toggle);
        _touchDebugToggle = toggle;
        _screen.RegisterExternalControl(toggle);
        var panel = new PanelContainer { Name = "RaceSexTouchDebugPanel", Position = new Vector2(12, 60),
            Size = new Vector2(Mathf.Max(280, size.X - 24), Mathf.Clamp(size.Y * 0.55f, 300, 560)),
            Visible = false, ZIndex = 4097, MouseFilter = Control.MouseFilterEnum.Stop };
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "Race/Sex touch pipeline · live diagnostics" });
        var feed = new TextEdit { Name = "RaceSexTouchDebugFeed", Editable = false,
            WrapMode = TextEdit.LineWrappingMode.Boundary, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 190) };
        column.AddChild(feed);
        _touchDebugFeed = feed;
        var actions = new HBoxContainer();
        column.AddChild(actions);
        var copy = new Button { Text = "COPY REPORT", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        copy.Pressed += () => { DisplayServer.ClipboardSet(string.Join("\n", _touchDebugEvents)); RecordTouchDiagnostic("report copied"); };
        actions.AddChild(copy);
        var clear = new Button { Text = "CLEAR", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        clear.Pressed += () => { _touchDebugEvents.Clear(); RefreshTouchDebugFeed(); RecordTouchDiagnostic("feed cleared"); };
        actions.AddChild(clear);
        var close = new Button { Text = "CLOSE", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        close.Pressed += () => panel.Visible = false;
        actions.AddChild(close);
        layer.AddChild(panel);
        _touchDebugPanel = panel;
        _screen.RegisterExternalControl(panel);
        RecordTouchDiagnostic($"overlay mounted viewport={size} entryLayer={Layer} overlayLayer={layer.Layer}");
    }

    private void RecordTouchDiagnostic(string detail)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} #{++_touchDebugSequence:D4} {detail}";
        _touchDebugEvents.Enqueue(line);
        while (_touchDebugEvents.Count > 160) _touchDebugEvents.Dequeue();
        GD.Print("OPENNV_RACESEX_TOUCH_REPORT " + line);
        RefreshTouchDebugFeed();
    }

    private void RefreshTouchDebugFeed()
    {
        if (_touchDebugFeed is not null) _touchDebugFeed.Text = string.Join("\n", _touchDebugEvents);
    }
    private string Text(string setting) => FalloutGameSettingStrings.Read(_records, setting);
    private NativeRaceSexChoice Link(int page) => new(_creation.Header(page) + " >", false, () => { _page = page; ShowPage(); }, Selectable: false);
    private NativeRaceSexChoice Slider(string setting, int min, int max, int jump, Func<int> get, Action<int> set, Func<string>? display = null)
        => new(Text(setting), false, () => { }, new(min, max, jump, get, set, display ?? (() => "")), false);
    private void Edit(Action action, bool rebuild = true)
    {
        action();
        if (rebuild) ShowPage();
    }
    private void ShowPage()
    {
        var state = _creation.Selection;
        IReadOnlyList<NativeRaceSexChoice> rows = _page switch
        {
            0 => [new(Text("sMale"), !state.Female, () => Edit(() => _creation.ChangeIdentity(state.RaceRuntimeFormId, false))),
                new(Text("sFemale"), state.Female, () => Edit(() => _creation.ChangeIdentity(state.RaceRuntimeFormId, true)))],
            1 => _creation.Races.Select(race => new NativeRaceSexChoice(race.DisplayName, race.RuntimeFormId == state.RaceRuntimeFormId,
                () => Edit(() => _creation.ChangeIdentity(race.RuntimeFormId, state.Female)))).ToArray(),
            2 => FaceRows(),
            3 => [Link(5), Link(6), Link(7)],
            4 => [Link(9), Link(19), Link(8)],
            5 => _creation.Race.HairFor(state.Female).OrderBy(part => part.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(part => new NativeRaceSexChoice(part.DisplayName, part.RuntimeFormId == state.HairRuntimeFormId, () => Edit(() => _creation.SetHair(part)))).ToArray(),
            6 => HairColorRows(),
            7 => _creation.FacialHair().Select(part => new NativeRaceSexChoice(part.DisplayName, state.Face!.HeadParts.Contains(part.RuntimeFormId),
                () => Edit(() => _creation.ToggleHeadPart(part)))).ToArray(),
            8 => _creation.Race.EyesFor(state.Female).OrderBy(part => part.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(part => new NativeRaceSexChoice(part.DisplayName, part.RuntimeFormId == state.EyesRuntimeFormId, () => Edit(() => _creation.SetEyes(part)))).ToArray(),
            9 => Enumerable.Range(10, 9).Select(Link).ToArray(),
            >= 10 and <= 19 => ControlRows(),
            _ => throw new NotSupportedException("Creation page has no native owner."),
        };
        _screen.Menu.SetPage(_page, _creation.Header(_page), rows, _page == 3);
    }
    private IReadOnlyList<NativeRaceSexChoice> FaceRows()
    {
        var rows = new List<NativeRaceSexChoice>(); var presets = _creation.Presets();
        if (presets.Count > 1) rows.Add(Slider("sRSMPreset", 1, presets.Count, 5, () => _creation.PresetIndex,
            value => _creation.ApplyPreset(value), () => _creation.PresetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        rows.Add(Link(4));
        rows.Add(new(Text("sRSMRandomize"), false, () => Confirm("sRSMConfirmRandomize", () =>
            throw new NotSupportedException("Native FaceGen random distribution and random stream are not yet bound.")), Selectable: false));
        rows.Add(Slider("sRSMAge", 1, 10, 3, () => _creation.AgeValue, _creation.SetAge));
        return rows;
    }
    private IReadOnlyList<NativeRaceSexChoice> HairColorRows()
    {
        var rows = new List<NativeRaceSexChoice>
        {
            Slider("sRSMPreset", 0, 15, 4, () => _creation.HairPresetIndex, _creation.SetHairPreset, () => _creation.HairPresetLabel),
        };
        string[] labels = ["sRSMRedAbbrev", "sRSMGreenAbbrev", "sRSMBlueAbbrev"];
        for (var channel = 0; channel < labels.Length; channel++)
        {
            var component = channel;
            rows.Add(Slider(labels[channel], 0, 255, 32, () => _creation.Selection.Face!.HairColor[component],
                value => _creation.SetHairComponent(component, value), () => _creation.Selection.Face!.HairColor[component].ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return rows;
    }
    private IReadOnlyList<NativeRaceSexChoice> ControlRows()
    {
        var controls = _creation.Controls.Controls.Where(row => row.Page == _page);
        if (_page == 19) controls = _creation.Controls.TextureOrder.Select(index => controls.Single(row => row.Index == index)).ToArray();
        return controls.Select(row =>
        {
            var limits = _creation.Limits(row);
            return Slider(row.Setting, limits.Minimum, limits.Maximum, Math.Max(1, (int)MathF.Ceiling((limits.Maximum - limits.Minimum) / 4f)),
                () => _creation.Value(row), value => _creation.SetControl(row, value));
        }).ToArray();
    }
    private void Navigate(int direction)
    {
        if (_confirmation is not null) return;
        if (direction > 0 && _page == 3) { Confirm("sRSMConfirmDone", () => Accepted?.Invoke(_creation.Selection)); return; }
        if (direction > 0) _page++;
        else _page = _page switch { <= 3 => Math.Max(0, _page - 1), 4 => 2, 5 or 6 or 7 => 3, 8 or 9 or 19 => 4, >= 10 and <= 18 => 9, _ => throw new NotSupportedException() };
        ShowPage();
    }
    private void Confirm(string setting, Action accepted)
    {
        _screen.SetProcessInput(false);
        var prompt = setting == "sRSMConfirmDone" ? $"{Text(setting)} {Text("sRSMCharacter")}?" : Text(setting);
        _confirmation = new(new(_records.RuntimeFormKey(_creation.Selection.RaceRuntimeFormId), "", prompt, true, [Text("sNo"), Text("sYes")]),
            _records, index =>
            {
                var menu = _confirmation!; _confirmation = null; RemoveChild(menu); menu.QueueFree();
                _screen.SetProcessInput(true);
                if (index == 1) { try { accepted(); } catch (Exception error) { Fail(error); } }
            }, Fail);
        AddChild(_confirmation);
    }
    private void RefreshPreview()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var appearance = _creation.Appearance();
        var resolved = System.Diagnostics.Stopwatch.GetTimestamp();
        var portrait = new NativeOwnedActorPreview(_records, appearance, _settings, (int)_screen.ContentView.Size.X);
        var constructed = System.Diagnostics.Stopwatch.GetTimestamp();
        portrait.SetIllustratedBackdrop(PortraitMode == ClassicPortraitMode.Illustrated);
        AddChild(portrait);
        try
        {
            // MTIdle is the engine's neutral standing animation group. Its
            // resource namespace follows the source actor's skeleton family.
            var skeleton = appearance.SkeletonPath.Replace('\\', '/');
            var path = skeleton[..skeleton.LastIndexOf('/')] + "/locomotion/mtidle.kf";
            var source = RuntimeLiveContentSource.Current!;
            if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException("Neutral source animation group is absent.", path);
            var file = FalloutNifFile.Read(bytes); var clip = file.Roots.Select(file.ReadControllerSequence).Single();
            portrait.Actor.PlayBaseSequence(file, clip, identity);
            portrait.UpdateProjection(); _screen.SetPortrait(portrait);
            if (_portrait is not null) { RemoveChild(_portrait); _portrait.QueueFree(); }
            _portrait = portrait; _previewRevision = _creation.Revision;
            if (OS.HasFeature("android"))
                GD.Print($"OPENNV_CREATION_PREVIEW_READY revision={_previewRevision} " +
                    $"appearanceMilliseconds={System.Diagnostics.Stopwatch.GetElapsedTime(started, resolved).TotalMilliseconds:F3} " +
                    $"constructionMilliseconds={System.Diagnostics.Stopwatch.GetElapsedTime(resolved, constructed).TotalMilliseconds:F3} " +
                    $"publicationMilliseconds={System.Diagnostics.Stopwatch.GetElapsedTime(constructed).TotalMilliseconds:F3}");
        }
        catch { RemoveChild(portrait); portrait.QueueFree(); throw; }
    }
    public override void _Process(double delta)
    {
        if (Error is not null) return;
        try
        {
            if (_portrait?.Actor.AnimationError is { } error) throw new InvalidOperationException(error);
            if (_previewRevision != _creation.Revision) RefreshPreview();
        }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error)
    {
        _background?.Dispose(); _background = null;
        Error = error.Message; GD.PushError($"OPENNV_NATIVE_CREATION_UNBOUND {error}"); Failed?.Invoke(error);
    }
    internal void ReleasePause()
    {
        _background?.Dispose(); _background = null;
        if (_pausedTree is null) return;
        _pausedTree.Paused = _previousPause; _pausedTree = null;
    }
    public override void _ExitTree() { GetViewport().SizeChanged -= Resize; ReleasePause(); }
}
