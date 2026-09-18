using Datamodel;
using SkiaSharp;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using Element = Datamodel.Element;

namespace VMapEdit.Cli;

public sealed class VmapEditor : IDisposable
{
    private readonly Datamodel.Datamodel document;
    private readonly Element root;
    private readonly Element world;

    public VmapEditor(Datamodel.Datamodel document)
    {
        this.document = document;
        root = document.Root ?? throw new InvalidDataException("VMAP document has no root element.");
        world = root.Get<Element>("world")!;
    }

    public static VmapEditor? Load(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        if (stream == null)
        {
            return null;
        }
        var document = Datamodel.Datamodel.Load(stream, Datamodel.Codecs.DeferredMode.Disabled);
        return new VmapEditor(document);
    }

    public IEnumerable<EntityRow> EnumerateEntities()
    {
        foreach (var entity in EnumerateEntityNodes(world, []))
        {
            yield return ToRow(entity);
        }
    }

    public IEnumerable<Element> EnumerateMapMeshes(Element node, HashSet<Guid> visited)
    {
        if (TryGetGuid(node, "id", out var id) && !visited.Add(id))
        {
            yield break;
        }

        if (node.ClassName == "CMapMesh")
        {
            yield return node;
        }
        else if (node.ClassName == "CMapInstance")
        {
            if (!TryGet<Element>(node, "target", out var instanceTargetElement) || instanceTargetElement is null)
            {
                yield break;
            }

            foreach (var val in EnumerateMapMeshes(node, visited))
            {
                yield return val;
            }
        }
        else
        {
            if (!TryGet<IList<Element>>(node, "children", out var children) || children is null)
            {
                yield break;
            }

            foreach (var child in children)
            {
                if (child == null)
                {
                    continue;
                }

                foreach (var descendant in EnumerateMapMeshes(child, visited))
                {
                    yield return descendant;
                }
            }
        }
    }

    public int ScaleMapMeshes(float factor)
    {
        return ScaleMapMeshesRecurse(world, [], factor, false, []);
    }
    private int ScaleMapMeshesRecurse(Element node, HashSet<Element> visited, float factor, bool onlyMeshOrigin, HashSet<string> visitedMapPrefabs)
    {
        var scaledCount = 0;
        if (!visited.Add(node))
        {
            return scaledCount;
        }

        if (!TryGet<IList<Element>>(node, "children", out var children) || children is null)
        {
            return scaledCount;
        }

        foreach (var child in children)
        {
            if (child.ClassName == "CMapMesh")
            {
                if (!TryGetVector3(child, "origin", out var originVal))
                {
                    continue;
                }

                // Scale the mesh's origin relative to the world origin
                child["origin"] = originVal * factor;

                if (!onlyMeshOrigin)
                {
                    var meshData = child.Get<Element>("meshData");
                    var vertexData = meshData.Get<Element>("vertexData");

                    var vertexDataStreams = vertexData.GetArray<Element>("streams");
                    foreach (var vertexDataStream in vertexDataStreams)
                    {
                        if (vertexDataStream.ClassName != "CDmePolygonMeshDataStream")
                            continue;

                        var data = vertexDataStream.GetArray<Vector3>("data");
                        for (int i = 0; i < data.Count; i++)
                        {
                            data[i] *= factor;
                        }
                    }
                }

                scaledCount++;
            }
            else if (child.ClassName == "CMapInstance")
            {
                if (!TryGet<Element>(child, "target", out var instanceTargetElement) || instanceTargetElement is null)
                {
                    continue;
                }

                scaledCount += ScaleMapMeshesRecurse(instanceTargetElement, visited, factor, false, visitedMapPrefabs);

                if (!TryGetVector3(child, "origin", out var originVal))
                {
                    continue;
                }

                // Scale the mesh's origin relative to the world origin
                child["origin"] = originVal * factor;
            }
            else if (child.ClassName == "CMapDeformerLattice")
            {
                if (!TryGetVector3(child, "origin", out var originVal))
                {
                    continue;
                }

                if (!TryGetVector3(child, "size", out var sizeVal))
                {
                    continue;
                }

                child["origin"] = originVal * factor;
                child["size"] = sizeVal * factor;

                var nodeData = child.Get<Element>("nodeData");
                var nodeDataControlPoints = nodeData.GetArray<Vector3>("controlPoints");

                for (int i = 0; i < nodeDataControlPoints.Count; i++)
                {
                    nodeDataControlPoints[i] = nodeDataControlPoints[i] * factor;
                }
                scaledCount += ScaleMapMeshesRecurse(child, visited, factor, false, visitedMapPrefabs);
            }
            else if (child.ClassName == "CMapGroup")
            {
                scaledCount += ScaleMapMeshesRecurse(child, visited, factor, false, visitedMapPrefabs);
            }
            else if (child.ClassName == "CMapPrefab")
            {
                if (!TryGetVector3(child, "origin", out var originVal))
                {
                    continue;
                }

                // Scale the mesh's origin relative to the world origin
                child["origin"] = originVal * factor;

                var targetMapPath = child.Get<string>("targetMapPath");
                if (String.IsNullOrEmpty(targetMapPath))
                {
                    continue;
                }
                var mapDir = Path.GetDirectoryName(targetMapPath);
                var mapName = Path.GetFileName(targetMapPath);
                if (mapName is null)
                {
                    Console.WriteLine("Failed to find map name from prefab path!");
                    continue;
                }

                var mapPathScaled = Path.Combine(mapDir, $"{Path.GetFileNameWithoutExtension(mapName)}_scaled.vmap");
                child["targetMapPath"] = mapPathScaled.Replace("\\", "/");
                if (!visitedMapPrefabs.Add(targetMapPath))
                {
                    continue;
                }

                var workdir = Directory.GetCurrentDirectory();
                var targetMapPathFull = Path.Combine(workdir, targetMapPath);
                // ! This does not handle loops!
                var mapPrefabFile = VmapEditor.Load(targetMapPathFull);
                if (mapPrefabFile is null)
                {
                    Console.WriteLine($"Failed to load map prefab file {targetMapPathFull} (please check that workdir is the addon root), skipping..." + targetMapPath);
                    continue;
                }
                var prefabScaledEntCount = mapPrefabFile.ScaleEntityOrigins(factor);
                var prefabMeshScaledCount = mapPrefabFile.ScaleMapMeshes(factor);


                var mapFullPath = Path.Combine(workdir, mapPathScaled);

                if (!Path.Exists(mapFullPath))
                {
                    Console.WriteLine($"Scaled {prefabScaledEntCount} entity origins by {factor.ToString(CultureInfo.InvariantCulture)} in prefab {targetMapPath}, saved as {mapPathScaled}.");
                    Console.WriteLine($"Scaled {prefabMeshScaledCount} meshes by {factor.ToString(CultureInfo.InvariantCulture)} in prefab {targetMapPath}, saved as {mapPathScaled}.");
                    using (var outFile = File.Create(mapFullPath))
                    {
                        mapPrefabFile.Save(outFile);
                    }
                    mapPrefabFile.Dispose();
                }
                else
                {
                    Console.WriteLine($"{mapFullPath} already exists, skipping...");
                }
            }
        }

        return scaledCount;
    }

    public int ScaleEntityOrigins(float factor)
    {
        var scaledCount = 0;

        foreach (var entity in EnumerateEntityNodes(world, []))
        {
            if (TryGetVector3(entity, "origin", out var origin))
            {
                entity["origin"] = origin * factor;
                scaledCount++;
            }

            if (TryGet(entity, "entity_properties", out Element entityProps))
            {
                if (!TryGetEntityProperty(entity, "classname", out var classname))
                {
                    continue;
                }

                if (TryGetVector3(entity, "scales", out var scale))
                {
                    entity["scales"] = scale * factor;
                }

                if (classname.Contains("light", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (TryGet(entityProps, "brightness_lumens", out string brightnessLumensStr))
                    {
                        if (Int32.TryParse(brightnessLumensStr, out var brightnessLumens))
                        {
                            // inverse-square law.
                            entityProps["brightness_lumens"] = (brightnessLumens * factor * factor).ToString(CultureInfo.InvariantCulture);
                        }
                    }

                    if (TryGet(entityProps, "range", out string rangeStr))
                    {
                        if (float.TryParse(rangeStr, CultureInfo.InvariantCulture, out var range))
                        {
                            entityProps["range"] = (range * factor).ToString(CultureInfo.InvariantCulture);
                        }
                    }

                    if (TryGetVector3FromStringProp(entityProps, "size_params", out var sizeParams))
                    {
                        Vector3 scaled = sizeParams * factor;
                        scaled.Z = sizeParams.Z * (1 / factor); // angle
                        entityProps["size_params"] = Vector3ToStringProp(scaled);
                    }

                    if (TryGetVector3FromStringProp(entityProps, "shear", out var shear))
                    {
                        entityProps["shear"] = Vector3ToStringProp(shear * factor);
                    }
                }
                else if (classname == "env_particle_glow")
                {
                    if (TryGet(entityProps, "scale", out string particleScaleStr))
                    {
                        if (float.TryParse(particleScaleStr, CultureInfo.InvariantCulture, out var particleScale))
                        {
                            entityProps["scale"] = (particleScale * factor).ToString(CultureInfo.InvariantCulture);
                        }
                    }
                }
                else if (classname == "cable_static" || classname == "cable_dynamic") // there are path_node_cable ents which we dont wanna touch, thus no 'contains' check
                {
                    if (TryGet(entity, "radius", out float radiusScale))
                    {
                        entity["radius"] = (radiusScale * factor);
                    }
                }
            }
        }

        return scaledCount;
    }

    public int UpdateEntityField(EntitySelector selector, string fieldName, string value)
    {
        var matches = EnumerateEntityNodes(world, [])
            .Where(entity => MatchesSelector(entity, selector))
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException("No entity matched the selector.");
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"Selector matched {matches.Count} entities. Add a more specific selector.");
        }

        var entity = matches[0];
        var entityProperties = entity.Get<Element>("entity_properties")!;
        entityProperties[fieldName] = value;
        return 1;
    }

    public void Save(Stream stream)
    {
        document.Save(stream, "binary", 9);
    }

    public void Dispose()
    {
        document.Dispose();
    }

    private static IEnumerable<Element> EnumerateEntityNodes(Element node, HashSet<Element> visited)
    {
        if (!visited.Add(node))
        {
            yield break;
        }

        if (TryGetElement(node, "entity_properties", out _))
        {
            yield return node;
        }

        if (!TryGet<IList<Element>>(node, "children", out var children) || children is null)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child == null)
            {
                continue;
            }

            foreach (var descendant in EnumerateEntityNodes(child, visited))
            {
                yield return descendant;
            }
        }
    }

    private static bool MatchesSelector(Element node, EntitySelector selector)
    {
        if (selector.NodeId is int nodeId && (!TryGetInt32(node, "nodeID", out var actualNodeId) || actualNodeId != nodeId))
        {
            return false;
        }

        if (selector.ClassName is string className)
        {
            if (!TryGetEntityProperty(node, "classname", out var actualClassName) ||
                !string.Equals(actualClassName, className, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (selector.TargetName is string targetName)
        {
            if (!TryGetEntityProperty(node, "targetname", out var actualTargetName) ||
                !string.Equals(actualTargetName, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetEntityProperty(Element node, string key, out string value)
    {
        if (!TryGetElement(node, "entity_properties", out var entityProperties))
        {
            value = string.Empty;
            return false;
        }

        return TryGetString(entityProperties, key, out value);
    }

    private static EntityRow ToRow(Element entity)
    {
        var className = TryGetEntityProperty(entity, "classname", out var parsedClassName) ? parsedClassName : null;
        var targetName = TryGetEntityProperty(entity, "targetname", out var parsedTargetName) ? parsedTargetName : null;
        var nodeId = TryGetInt32(entity, "nodeID", out var parsedNodeId) ? parsedNodeId : (int?)null;
        var origin = TryGetVector3(entity, "origin", out var parsedOrigin) ? parsedOrigin : (Vector3?)null;
        var id = TryGetGuid(entity, "id", out var parsedId) ? parsedId.ToString() : null;

        return new EntityRow(className, targetName, nodeId, origin, id);
    }

    private static bool TryGetElement(Element element, string key, out Element value)
        => TryGet(element, key, out value);

    private static bool TryGetString(Element element, string key, out string value)
        => TryGet(element, key, out value);

    private static bool TryGetInt32(Element element, string key, out int value)
        => TryGet(element, key, out value);

    private static bool TryGetGuid(Element element, string key, out Guid value)
        => TryGet(element, key, out value);

    private static bool TryGetVector3(Element element, string key, out Vector3 value)
        => TryGet(element, key, out value);

    private static bool TryGet<T>(Element element, string key, out T value)
    {
        try
        {
            value = element.Get<T>(key)!;
            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }

    public bool TryGetVector3FromStringProp(Element element, string key, out Vector3 value)
    {
        if(TryGet(element, key, out string prop))
        {
            var vals = prop.Split(' ');
            if(vals.Length != 3)
            {
                value = new();
                return false;
            }

            float x = 0;
            float y = 0;
            float z = 0;
            if (float.TryParse(vals[0], CultureInfo.InvariantCulture, out var outX))
            {
                x = outX;
            }
            if (float.TryParse(vals[1], CultureInfo.InvariantCulture, out var outY))
            {
                y = outY;
            }
            if (float.TryParse(vals[2], CultureInfo.InvariantCulture, out var outZ))
            {
                z = outZ;
            }

            value = new(x, y, z);
            return true;
        }

        value = new();
        return false;
    }

    public string Vector3ToStringProp(Vector3 vec)
    {
        FormattableString fmtString = $"{vec.X:0.000000} {vec.Y:0.000000} {vec.Z:0.000000}";
        return fmtString.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed record EntityRow(string? ClassName, string? TargetName, int? NodeId, Vector3? Origin, string? Id)
{
    public string FormatLine()
    {
        var originText = Origin is { } origin
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###}", origin.X, origin.Y, origin.Z)
            : "<no origin>";

        var classText = string.IsNullOrWhiteSpace(ClassName) ? "<no classname>" : ClassName;
        var targetText = string.IsNullOrWhiteSpace(TargetName) ? string.Empty : $" targetname={TargetName}";
        var nodeText = NodeId is int nodeId ? $" nodeID={nodeId.ToString(CultureInfo.InvariantCulture)}" : string.Empty;

        return $"{classText}{nodeText}{targetText} origin={originText}";
    }
}

public sealed record EntitySelector
{
    public string? ClassName { get; init; }
    public string? TargetName { get; init; }
    public int? NodeId { get; init; }
}