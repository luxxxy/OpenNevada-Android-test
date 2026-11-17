using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal static class RuntimeSaveLoadViewNumericContracts
{
    internal const float BackdropOpacity = 0.88f;
    internal const float MinimumWidth = 520.0f;
    internal const float RowHeight = 48.0f;
    internal const int Layer = 121;
    internal const int TitleFontSize = 32;
    internal const int BodyFontSize = 16;
    internal const int Spacing = 14;
}

internal sealed partial class RuntimeSaveLoadView : CanvasLayer
{
    private readonly ItemList _slots = new();
    private readonly Label _backupStatus = new();
    private RuntimeSaveSlotCatalog _catalog = null!;
    private Action? _save;
    private IReadOnlyList<RuntimeSaveSlotMetadata> _metadata = Array.Empty<RuntimeSaveSlotMetadata>();

    internal event Action? CloseRequested;
    internal event Action<RuntimeSaveSlotMetadata>? LoadRequested;

    internal void Configure(RuntimeSaveSlotCatalog catalog, Action? save)
    {
        _catalog = catalog;
        _save = save;
        Name = "OpenNVSaveLoad";
        Layer = RuntimeSaveLoadViewNumericContracts.Layer;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        var background = new ColorRect
        {
            Color = new Color(Colors.Black, RuntimeSaveLoadViewNumericContracts.BackdropOpacity),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);
        var stack = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(
                RuntimeSaveLoadViewNumericContracts.MinimumWidth,
                RuntimeSaveLoadViewNumericContracts.RowHeight),
        };
        stack.AddThemeConstantOverride("separation", RuntimeSaveLoadViewNumericContracts.Spacing);
        center.AddChild(stack);
        stack.AddChild(Label("SAVE / LOAD", RuntimeSaveLoadViewNumericContracts.TitleFontSize));
        _slots.Name = "AuthoritativeSaveSlots";
        _slots.CustomMinimumSize = new Vector2(
            RuntimeSaveLoadViewNumericContracts.MinimumWidth,
            RuntimeSaveLoadViewNumericContracts.RowHeight * 4.0f);
        stack.AddChild(_slots);
        if (_save is not null)
        {
            var create = Button("CREATE SAVE");
            create.Name = "CreateAuthoritativeSaveSlot";
            create.Pressed += Create;
            stack.AddChild(create);
        }
        var load = Button("LOAD SELECTED");
        load.Name = "LoadSelectedAuthoritativeSaveSlot";
        load.Pressed += LoadSelected;
        stack.AddChild(load);
        var export = Button("EXPORT BACKUP");
        export.Name = "ExportCampaignSaveBackup";
        export.Pressed += ExportBackup;
        stack.AddChild(export);
        var import = Button("IMPORT LATEST BACKUP");
        import.Name = "ImportCampaignSaveBackup";
        import.Pressed += ImportLatestBackup;
        stack.AddChild(import);
        _backupStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _backupStatus.AddThemeFontSizeOverride("font_size", 13);
        stack.AddChild(_backupStatus);
        var done = Button("DONE");
        done.Pressed += Dismiss;
        stack.AddChild(done);
        Refresh();
        GetTree().Paused = true;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Dismiss();
        }
    }

    public override void _ExitTree() => GetTree().Paused = false;

    internal RuntimeSaveSlotMetadata CreateForProof()
    {
        var slot = CreateSlot();
        Select(slot.Id);
        return slot;
    }

    internal void LoadSelectedForProof() => LoadSelected();

    private void Create()
    {
        var slot = CreateSlot();
        Select(slot.Id);
    }

    private RuntimeSaveSlotMetadata CreateSlot()
    {
        var slot = _catalog.Create(_save ?? throw new InvalidOperationException(
            "Save creation is not available without an active authoritative session."));
        Refresh();
        return slot;
    }

    private static string BackupDirectory()
    {
        var root = OS.GetName() == "Android"
            ? "/storage/emulated/0/Documents"
            : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(root))
            root = ProjectSettings.GlobalizePath("user://");
        return Path.Combine(root, "OpenNV-Backups");
    }

    private void ExportBackup()
    {
        try
        {
            var directory = BackupDirectory();
            Directory.CreateDirectory(directory);
            var fileName = "opennv-save-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".zip";
            var path = Path.Combine(directory, fileName);
            _catalog.ExportBackup(path);
            _backupStatus.Text = "Backup exported: " + path;
            _backupStatus.AddThemeColorOverride("font_color", new Color(0.47f, 0.84f, 0.62f));
        }
        catch (Exception exception)
        {
            _backupStatus.Text = "Backup export failed: " + exception.Message;
            _backupStatus.AddThemeColorOverride("font_color", new Color(0.86f, 0.61f, 0.33f));
        }
    }

    private void ImportLatestBackup()
    {
        try
        {
            var directory = BackupDirectory();
            var latest = Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "opennv-save-*.zip", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (latest is null)
                throw new FileNotFoundException("No backup found. Place an opennv-save-*.zip file in " + directory);
            var importedSlots = _catalog.ImportBackup(latest);
            var current = _catalog.ReadSlots(includeCurrent: true)
                .FirstOrDefault(slot => slot.Id == "current")
                ?? throw new InvalidDataException("Imported current save could not be read back.");
            _backupStatus.Text = $"Imported {Path.GetFileName(latest)} with {importedSlots} save slot(s).";
            _backupStatus.AddThemeColorOverride("font_color", new Color(0.47f, 0.84f, 0.62f));
            GetTree().Paused = false;
            LoadRequested?.Invoke(current);
        }
        catch (Exception exception)
        {
            _backupStatus.Text = "Backup import failed: " + exception.Message;
            _backupStatus.AddThemeColorOverride("font_color", new Color(0.86f, 0.61f, 0.33f));
        }
    }

    private void LoadSelected()
    {
        var selected = _slots.GetSelectedItems();
        if (selected.Length != 1)
            return;
        var slot = _catalog.Activate(_metadata[selected[0]].Id);
        GetTree().Paused = false;
        LoadRequested?.Invoke(slot);
    }

    private void Select(string id)
    {
        var index = _metadata.ToList().FindIndex(row => row.Id == id);
        if (index >= 0)
            _slots.Select(index);
    }

    private void Refresh()
    {
        _metadata = _catalog.ReadSlots();
        _slots.Clear();
        foreach (var slot in _metadata)
        {
            var owner = string.IsNullOrWhiteSpace(slot.CharacterName) ? "SAVED GAME" : slot.CharacterName;
            var map = string.IsNullOrWhiteSpace(slot.MapName) ? "CURRENT MAP" : slot.MapName;
            var hp = slot.HitPoints is { } value ? $"  •  HP {value}" : string.Empty;
            _slots.AddItem($"{owner}  •  {map}{hp}  •  {slot.WrittenUtc:u}");
        }
        if (_metadata.Count > 0)
            _slots.Select(0);
    }

    private void Dismiss()
    {
        GetTree().Paused = false;
        CloseRequested?.Invoke();
    }

    private static Button Button(string text) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(
            RuntimeSaveLoadViewNumericContracts.MinimumWidth,
            RuntimeSaveLoadViewNumericContracts.RowHeight),
    };

    private static Label Label(string text, int size)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}
