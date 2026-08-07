using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LatticeModifier))]
[CanEditMultipleObjects]
public class LatticeModifierEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LatticeModifier modifier = (LatticeModifier)target;

        EditorGUILayout.Space();

        if (modifier.Lattice == null)
        {
            EditorGUILayout.HelpBox(
                "No cage assigned. Use the button below, or drag an existing Lattice Deformer " +
                "into the Lattice field.", MessageType.Warning);

            if (GUILayout.Button("Create Lattice Around This Mesh"))
            {
                CreateLatticeFor(modifier);
            }
            return;
        }

        EditorGUILayout.LabelField("Vertices", modifier.VertexCount.ToString("N0"));

        if (modifier.VertexCount > 30000)
        {
            EditorGUILayout.HelpBox(
                "This mesh is heavy for a CPU deformer. It is fine while the cage is static " +
                "(nothing recomputes), but animating the cage at this vertex count will cost " +
                "real frame time - especially with Recalculate Normals on.", MessageType.Info);
        }

        if (GUILayout.Button("Rebind"))
        {
            modifier.Bind();
        }
    }

    /// <summary>
    /// Creates a cage already sized to the mesh and parented to it, which is the setup you
    /// want most of the time.
    /// </summary>
    [MenuItem("GameObject/Deformers/Lattice For Selected Mesh", false, 10)]
    private static void CreateForSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null) return;

        LatticeModifier modifier = selected.GetComponent<LatticeModifier>();
        if (modifier == null)
        {
            modifier = Undo.AddComponent<LatticeModifier>(selected);
        }

        CreateLatticeFor(modifier);
    }

    [MenuItem("GameObject/Deformers/Lattice For Selected Mesh", true)]
    private static bool ValidateCreateForSelection()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null && selected.GetComponent<MeshFilter>() != null;
    }

    private static void CreateLatticeFor(LatticeModifier modifier)
    {
        GameObject cage = new GameObject("Lattice");
        Undo.RegisterCreatedObjectUndo(cage, "Create Lattice");
        Undo.SetTransformParent(cage.transform, modifier.transform, "Create Lattice");

        cage.transform.localPosition = Vector3.zero;
        cage.transform.localRotation = Quaternion.identity;
        cage.transform.localScale = Vector3.one;

        LatticeDeformer deformer = Undo.AddComponent<LatticeDeformer>(cage);

        Renderer renderer = modifier.GetComponent<Renderer>();
        if (renderer != null)
        {
            deformer.FitTo(renderer.bounds, renderer.bounds.extents.magnitude * 0.02f);
        }

        Undo.RecordObject(modifier, "Create Lattice");
        modifier.Lattice = deformer;
        modifier.Bind();

        EditorUtility.SetDirty(modifier);
        Selection.activeGameObject = cage;
    }
}
