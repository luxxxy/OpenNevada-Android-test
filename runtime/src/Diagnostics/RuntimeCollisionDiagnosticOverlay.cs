using Godot;

namespace OpenNV.Runtime.Diagnostics;

// On-device report for diagnosing a missing landscape floor without ADB.
internal sealed partial class RuntimeCollisionDiagnosticOverlay : CanvasLayer
{
    private Label _report = null!;
    private Button _copy = null!;
    private double _elapsed;

    public override void _Ready()
    {
        Layer = 210;
        ProcessMode = ProcessModeEnum.Always;
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        panel.OffsetLeft = 12;
        panel.OffsetTop = 12;
        panel.OffsetRight = 452;
        panel.OffsetBottom = 292;
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, .88f) };
        style.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        panel.AddChild(column);
        var heading = new Label { Text = "OPENNV COLLISION DIAGNOSTICS", MouseFilter = Control.MouseFilterEnum.Ignore };
        heading.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(heading);
        _report = new Label
        {
            Text = "Waiting for world and player…",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _report.AddThemeFontSizeOverride("font_size", 11);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 218), SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        _report.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_report);
        _copy = new Button { Text = "COPY REPORT", CustomMinimumSize = new Vector2(0, 32) };
        _copy.Pressed += () => DisplayServer.ClipboardSet(_report.Text);
        column.AddChild(_copy);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < .5) return;
        _elapsed = 0;
        _report.Text = BuildReport();
    }

    private string BuildReport()
    {
        var lines = new List<string>
        {
            $"Physics: {ProjectSettings.GetSetting("physics/3d/physics_engine", "unknown").AsString()}",
            $"Frames: {Engine.GetFramesPerSecond()} FPS"
        };
        var nodes = Descendants(GetTree().Root).ToArray();
        var cellRoots = nodes.OfType<Node3D>()
            .Where(node => node.HasMeta("opennv_land_candidate_meshes") ||
                node.Name.ToString().StartsWith("CELL_", StringComparison.OrdinalIgnoreCase) || node.Name.ToString().StartsWith("NativeCell_", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (cellRoots.Length == 0)
            lines.Add("CELL ROOTS: none found");
        foreach (var cell in cellRoots)
        {
            var hasLandMeta = cell.HasMeta("opennv_land_candidate_meshes");
            var candidates = hasLandMeta ? cell.GetMeta("opennv_land_candidate_meshes").AsInt32() : -1;
            var requested = hasLandMeta && cell.GetMeta("opennv_land_collision_requested").AsBool();
            var created = hasLandMeta && cell.GetMeta("opennv_land_collision_created").AsBool();
            var landBody = Descendants(cell).OfType<StaticBody3D>()
                .FirstOrDefault(body => body.Name == "LAND_ACTIVE_SET_COLLISION");
            var shape = landBody is null ? null : Descendants(landBody).OfType<CollisionShape3D>().FirstOrDefault();
            lines.Add(hasLandMeta
                ? $"CELL {cell.Name}: LAND meshes={candidates}, requested={requested}, created={created}"
                : $"CELL {cell.Name}: LAND metadata MISSING");
            lines.Add(landBody is null
                ? "  LAND body: MISSING"
                : $"  LAND body: layer={landBody.CollisionLayer}, pos={Vec(landBody.GlobalPosition)}, scale={Vec(landBody.GlobalBasis.Scale)}");
            lines.Add(shape is null
                ? "  LAND shape: MISSING"
                : $"  LAND shape: {shape.Shape?.GetType().Name ?? "null"}, disabled={shape.Disabled}");
            if (landBody is not null && landBody.HasMeta("opennv_land_triangles"))
                lines.Add($"  LAND triangles={landBody.GetMeta("opennv_land_triangles").AsInt32()}");
        }

        var players = nodes.OfType<CharacterBody3D>()
            .Where(body => body.Name.ToString().Contains("Player", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (players.Length == 0)
            lines.Add("Player CharacterBody3D: NOT FOUND");
        foreach (var player in players)
        {
            lines.Add($"PLAYER {player.Name}: pos={Vec(player.GlobalPosition)} vel={Vec(player.Velocity)}");
            lines.Add($"  mask={player.CollisionMask} layer={player.CollisionLayer} onFloor={player.IsOnFloor()} contacts={player.GetSlideCollisionCount()}");
            var from = player.GlobalPosition + Vector3.Up * 2.0f;
            var to = player.GlobalPosition - Vector3.Up * 40.0f;
            var query = PhysicsRayQueryParameters3D.Create(from, to, player.CollisionMask);
            query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
            query.CollideWithAreas = false;
            query.CollideWithBodies = true;
            var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0)
                lines.Add("  DOWN RAY (player mask): NO HIT (42m)");
            else
            {
                var collider = hit["collider"].AsGodotObject() as Node;
                var point = hit["position"].AsVector3();
                lines.Add($"  DOWN RAY (player mask): HIT {collider?.GetPath().ToString() ?? "unknown"} at {Vec(point)}");
            }

            var allLayersQuery = PhysicsRayQueryParameters3D.Create(from, to, uint.MaxValue);
            allLayersQuery.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
            allLayersQuery.CollideWithAreas = false;
            allLayersQuery.CollideWithBodies = true;
            var allLayersHit = player.GetWorld3D().DirectSpaceState.IntersectRay(allLayersQuery);
            if (allLayersHit.Count == 0)
                lines.Add("  DOWN RAY (all layers): NO HIT (42m)");
            else
            {
                var collider = allLayersHit["collider"].AsGodotObject() as CollisionObject3D;
                var point = allLayersHit["position"].AsVector3();
                lines.Add($"  DOWN RAY (all layers): HIT {collider?.Name} layer={collider?.CollisionLayer} at {Vec(point)}");
            }
        }

        var staticBodies = nodes.OfType<StaticBody3D>().ToArray();
        var shapes = nodes.OfType<CollisionShape3D>().ToArray();
        var layerSummary = staticBodies
            .GroupBy(body => body.CollisionLayer)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}:{group.Count()}");
        lines.Add($"StaticBody layers: {(layerSummary.Any() ? string.Join(", ", layerSummary) : "none")}");
        lines.Add($"Scene totals: staticBodies={staticBodies.Length}, collisionShapes={shapes.Length}, meshes={nodes.OfType<MeshInstance3D>().Count()}");
        foreach (var body in staticBodies.Take(12))
        {
            var bodyShapes = Descendants(body).OfType<CollisionShape3D>().Count();
            lines.Add($"BODY {body.GetPath()} layer={body.CollisionLayer} mask={body.CollisionMask} pos={Vec(body.GlobalPosition)} shapes={bodyShapes}");
        }
        var rigidBodies = nodes.OfType<RigidBody3D>().ToArray();
        lines.Add($"RigidBodies={rigidBodies.Length}");
        foreach (var body in rigidBodies.Take(8))
            lines.Add($"RIGID {body.Name} layer={body.CollisionLayer} mask={body.CollisionMask} pos={Vec(body.GlobalPosition)}");
        var sceneNames = GetTree().CurrentScene?.GetChildren()
            .Select(node => node.Name.ToString()).Take(20).ToArray() ?? Array.Empty<string>();
        lines.Add($"ROOT CHILDREN: {(sceneNames.Length == 0 ? "none" : string.Join(", ", sceneNames))}");
        return string.Join("\n", lines);
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            foreach (var child in current.GetChildren())
                pending.Push(child);
        }
    }

    private static string Vec(Vector3 value) =>
        $"({value.X:F2}, {value.Y:F2}, {value.Z:F2})";
}
