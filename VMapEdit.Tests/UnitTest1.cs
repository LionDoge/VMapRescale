using System.Numerics;

using Datamodel;

using VMapEdit.Cli;

namespace VMapEdit.Tests;

public class VmapEditorTests
{
    [Fact]
    public void ScaleEntityOrigins_UpdatesNestedEntities()
    {
        using var editor = CreateEditor();

        var scaled = editor.ScaleEntityOrigins(2f);

        Assert.Equal(1, scaled);

        var entity = editor.EnumerateEntities().Single(row => row.ClassName == "prop_dynamic");
        Assert.Equal(new Vector3(2, 4, 6), entity.Origin);
    }

    [Fact]
    public void UpdateEntityField_ChangesEntityProperty()
    {
        using var editor = CreateEditor();

        var updated = editor.UpdateEntityField(new EntitySelector { NodeId = 2 }, "targetname", "crate_02");

        Assert.Equal(1, updated);

        var entity = editor.EnumerateEntities().Single(row => row.NodeId == 2);
        Assert.Equal("crate_02", entity.TargetName);
    }

    [Fact]
    public void Save_RoundTripsThroughBinaryDmx()
    {
        using var editor = CreateEditor();
        using var stream = new MemoryStream();

        editor.Save(stream);
        stream.Position = 0;

        var reloaded = Datamodel.Datamodel.Load(stream, Datamodel.Codecs.DeferredMode.Disabled);
        Assert.Equal("vmap", reloaded.Format);
        Assert.NotNull(reloaded.Root);
    }

    private static VmapEditor CreateEditor()
    {
        var document = new Datamodel.Datamodel("vmap", 29);
        var root = new Element(document, "root");
        var world = new Element(document, "world");
        var worldProperties = new Element(document, "entity_properties");
        var entity = new Element(document, "entity");
        var entityProperties = new Element(document, "entity_properties");

        worldProperties["classname"] = "worldspawn";
        world["entity_properties"] = worldProperties;

        entityProperties["classname"] = "prop_dynamic";
        entityProperties["targetname"] = "crate_01";
        entity["nodeID"] = 2;
        entity["origin"] = new Vector3(1, 2, 3);
        entity["entity_properties"] = entityProperties;
        world["children"] = new ElementArray(new[] { entity });

        root["world"] = world;
        document.Root = root;

        return new VmapEditor(document);
    }
}
