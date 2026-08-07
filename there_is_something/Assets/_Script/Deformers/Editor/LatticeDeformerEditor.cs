using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view editing for <see cref="LatticeDeformer"/>: click points to select, shift-click to
/// add, and drag one gizmo to move the whole selection.
/// </summary>
[CustomEditor(typeof(LatticeDeformer))]
public class LatticeDeformerEditor : Editor
{
    private const int MaxInteractivePoints = 4096;

    private readonly HashSet<int> selection = new HashSet<int>();
    private readonly List<LatticeModifier> boundModifiers = new List<LatticeModifier>();

    private LatticeDeformer deformer;

    private void OnEnable()
    {
        deformer = (LatticeDeformer)target;
        RefreshBoundModifiers();
    }

    // ------------------------------------------------------------------ inspector

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        int count = deformer.PointCount;
        EditorGUILayout.LabelField("Control Points", count.ToString("N0") +
                                   "   (" + selection.Count + " selected)");

        if (count > MaxInteractivePoints)
        {
            EditorGUILayout.HelpBox(
                "Above " + MaxInteractivePoints.ToString("N0") + " points the scene-view " +
                "handles are hidden to keep the editor responsive. The deformation still works.",
                MessageType.Info);
        }

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select All"))
            {
                selection.Clear();
                for (int i = 0; i < count; i++) selection.Add(i);
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(selection.Count == 0))
            {
                if (GUILayout.Button("Clear Selection"))
                {
                    selection.Clear();
                    SceneView.RepaintAll();
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reset Points"))
            {
                Undo.RecordObject(deformer, "Reset Lattice Points");
                deformer.ResetPoints();
                Changed();
            }

            if (GUILayout.Button("Fit To Bound Meshes"))
            {
                FitToBoundMeshes();
            }
        }

        if (GUILayout.Button("Refresh Bound Mesh List"))
        {
            RefreshBoundModifiers();
        }

        EditorGUILayout.LabelField("Deforming", boundModifiers.Count + " mesh(es)");

        if (boundModifiers.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No LatticeModifier points at this cage yet. Add a Lattice Modifier component " +
                "to the mesh you want to deform and assign this object to its Lattice field.",
                MessageType.Info);
        }
    }

    // ------------------------------------------------------------------ scene view

    private void OnSceneGUI()
    {
        Vector3Int res = deformer.Resolution;
        int count = deformer.PointCount;
        if (count > MaxInteractivePoints) return;

        // Resolution may have changed under us, which invalidates stored indices.
        selection.RemoveWhere(i => i < 0 || i >= count);

        Transform t = deformer.transform;
        Event e = Event.current;

        // Point handles. Drawn as buttons so a click selects rather than drags - dragging is
        // handled by the single position handle below, which is what makes moving a group of
        // points possible at all.
        for (int z = 0; z < res.z; z++)
        {
            for (int y = 0; y < res.y; y++)
            {
                for (int x = 0; x < res.x; x++)
                {
                    int index = deformer.GetIndex(x, y, z);
                    Vector3 world = t.TransformPoint(deformer.GetPosition(x, y, z));

                    bool isSelected = selection.Contains(index);
                    Handles.color = isSelected ? Color.yellow : new Color(0.4f, 0.8f, 1f, 0.9f);

                    float size = HandleUtility.GetHandleSize(world) * (isSelected ? 0.055f : 0.04f);

                    if (Handles.Button(world, Quaternion.identity, size, size * 1.6f, Handles.DotHandleCap))
                    {
                        if (e.shift)
                        {
                            if (!selection.Add(index)) selection.Remove(index);  // shift toggles
                        }
                        else
                        {
                            selection.Clear();
                            selection.Add(index);
                        }
                        Repaint();
                    }
                }
            }
        }

        if (selection.Count == 0) return;

        // One handle at the centroid of the selection; its delta is applied to every selected
        // point, so a whole face or row moves together.
        Vector3 centroidLocal = Vector3.zero;
        foreach (int index in selection) centroidLocal += LocalPositionOf(index, res);
        centroidLocal /= selection.Count;

        Vector3 centroidWorld = t.TransformPoint(centroidLocal);
        Quaternion rotation = Tools.pivotRotation == PivotRotation.Local ? t.rotation : Quaternion.identity;

        EditorGUI.BeginChangeCheck();
        Vector3 movedWorld = Handles.PositionHandle(centroidWorld, rotation);

        if (EditorGUI.EndChangeCheck())
        {
            // InverseTransformVector, not InverseTransformPoint: this is a displacement, and it
            // must respect the object's rotation and scale without picking up its translation.
            Vector3 localDelta = t.InverseTransformVector(movedWorld - centroidWorld);

            Undo.RecordObject(deformer, "Move Lattice Points");

            foreach (int index in selection)
            {
                Unflatten(index, res, out int x, out int y, out int z);
                deformer.SetOffset(x, y, z, deformer.GetOffset(x, y, z) + localDelta);
            }

            Changed();
        }
    }

    // ------------------------------------------------------------------ helpers

    private Vector3 LocalPositionOf(int index, Vector3Int res)
    {
        Unflatten(index, res, out int x, out int y, out int z);
        return deformer.GetPosition(x, y, z);
    }

    private static void Unflatten(int index, Vector3Int res, out int x, out int y, out int z)
    {
        x = index % res.x;
        y = (index / res.x) % res.y;
        z = index / (res.x * res.y);
    }

    private void FitToBoundMeshes()
    {
        RefreshBoundModifiers();

        bool any = false;
        Bounds bounds = new Bounds();

        foreach (LatticeModifier modifier in boundModifiers)
        {
            Renderer renderer = modifier.GetComponent<Renderer>();
            if (renderer == null) continue;

            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        if (!any)
        {
            Debug.LogWarning("Nothing to fit to - no LatticeModifier with a Renderer points at " +
                             "this cage.", deformer);
            return;
        }

        Undo.RecordObject(deformer, "Fit Lattice");
        // A little padding, so the outermost cage points sit just off the surface rather than
        // exactly on it - dragging a point that is coplanar with the mesh is fiddly.
        deformer.FitTo(bounds, bounds.extents.magnitude * 0.02f);
        Changed();
    }

    private void RefreshBoundModifiers()
    {
        boundModifiers.Clear();

        LatticeModifier[] all = Object.FindObjectsByType<LatticeModifier>(FindObjectsInactive.Include);

        foreach (LatticeModifier modifier in all)
        {
            if (modifier.Lattice == deformer) boundModifiers.Add(modifier);
        }
    }

    /// <summary>
    /// Marks the cage dirty and pushes the result straight to the bound meshes. Waiting for the
    /// modifier's own LateUpdate works, but pushing here is what makes dragging feel live.
    /// </summary>
    private void Changed()
    {
        EditorUtility.SetDirty(deformer);

        for (int i = boundModifiers.Count - 1; i >= 0; i--)
        {
            LatticeModifier modifier = boundModifiers[i];
            if (modifier == null) { boundModifiers.RemoveAt(i); continue; }
            if (modifier.IsBound) modifier.Deform();
        }

        SceneView.RepaintAll();
    }
}
