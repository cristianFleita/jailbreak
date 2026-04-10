using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Jailbreak.NPC;

namespace Jailbreak.Editor
{
    /// <summary>
    /// Creates a WaypointRegistry GameObject in the current scene with all
    /// waypoint IDs pre-populated. Assign the scene GameObjects in the Inspector.
    ///
    /// Run via: Jailbreak → Setup WaypointRegistry in Scene
    /// </summary>
    public static class WaypointRegistryGenerator
    {
        // ─── Entry Definition (build-time only) ──────────────────────────────

        private struct WPDef
        {
            public string   id;
            public string   zone;
            public string   subZone;
            public int      cap;
            public string[] phases;

            public WPDef(string id, string zone, string subZone, int cap, params string[] phases)
            {
                this.id      = id;
                this.zone    = zone;
                this.subZone = subZone;
                this.cap     = cap;
                this.phases  = phases;
            }
        }

        // ─── Menu Item ────────────────────────────────────────────────────────

        [MenuItem("Jailbreak/Setup WaypointRegistry in Scene")]
        public static void Generate()
        {
            // Check if one already exists in the scene
            var existing = Object.FindObjectOfType<WaypointRegistry>();
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog(
                    "WaypointRegistry already exists",
                    "A WaypointRegistry is already in the scene. Overwrite it?\n" +
                    "All existing GameObject assignments will be lost.",
                    "Overwrite", "Cancel"))
                    return;

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var go       = new GameObject("WaypointRegistry");
            var registry = go.AddComponent<WaypointRegistry>();
            Undo.RegisterCreatedObjectUndo(go, "Create WaypointRegistry");

            var so   = new SerializedObject(registry);
            var list = so.FindProperty("waypoints");
            list.ClearArray();

            var defs = BuildAllWaypoints();
            foreach (var def in defs)
                AppendEntry(list, def);

            so.ApplyModifiedProperties();

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log($"[WaypointRegistry] Created {defs.Count} waypoint slots in scene. " +
                      "Select the WaypointRegistry GameObject and assign Transforms in the Inspector.");
        }

        // ─── Waypoint Definitions ─────────────────────────────────────────────

        private static List<WPDef> BuildAllWaypoints()
        {
            var d = new List<WPDef>();

            // ── Fase 1: Inicio ────────────────────────────────────────────────
            // 20 spawn positions, one per NPC, outside each cell door
            for (int i = 1; i <= 20; i++)
                d.Add(new WPDef($"cell_door_exit_{i:D2}", "celda", "", 1, "1"));

            // ── Comedor: Fases 1 (entry), 2, 5, 8 ───────────────────────────
            for (int i = 1; i <= 5; i++)
                d.Add(new WPDef($"cafeteria_path_{i:D2}", "comedor", "", 4, "1", "2", "5", "8"));

            for (int i = 1; i <= 16; i++)
                d.Add(new WPDef($"cafeteria_seat_{i:D2}", "comedor", "", 2, "2", "4", "5", "8"));

            for (int i = 1; i <= 6; i++)
                d.Add(new WPDef($"cafeteria_counter_{i:D2}", "comedor", "", 1, "2", "5", "8"));

            for (int i = 1; i <= 8; i++)
                d.Add(new WPDef($"cafeteria_line_{i:D2}", "comedor", "", 1, "2", "4", "5", "8"));

            for (int i = 1; i <= 3; i++)
                d.Add(new WPDef($"cafeteria_tray_deposit_{i:D2}", "comedor", "", 1, "2", "5", "8"));

            // ── Fases 3 y 6: Trabajo — Taller ────────────────────────────────
            for (int i = 1; i <= 6; i++)
                d.Add(new WPDef($"workshop_bench_{i:D2}", "trabajo", "taller", 1, "3", "6"));

            for (int i = 1; i <= 4; i++)
                d.Add(new WPDef($"workshop_shelf_{i:D2}", "trabajo", "taller", 1, "3", "6"));

            for (int i = 1; i <= 4; i++)
                d.Add(new WPDef($"workshop_machine_{i:D2}", "trabajo", "taller", 1, "3", "6"));

            for (int i = 1; i <= 3; i++)
                d.Add(new WPDef($"workshop_chat_spot_{i:D2}", "trabajo", "taller", 2, "3", "6"));

            // ── Fases 3, 4 y 6: Lavandería ───────────────────────────────────
            for (int i = 1; i <= 6; i++)
                d.Add(new WPDef($"laundry_washer_{i:D2}", "lavanderia", "lavanderia", 1, "3", "4", "6"));

            for (int i = 1; i <= 6; i++)
                d.Add(new WPDef($"laundry_fold_{i:D2}", "lavanderia", "lavanderia", 1, "3", "4", "6"));

            for (int i = 1; i <= 4; i++)
                d.Add(new WPDef($"laundry_dryer_{i:D2}", "lavanderia", "lavanderia", 1, "3", "4", "6"));

            // ── Fase 4: Hora libre — Patio ────────────────────────────────────
            for (int i = 1; i <= 8; i++)
                d.Add(new WPDef($"yard_perimeter_{i:D2}", "patio", "patio", 1, "4"));

            for (int i = 1; i <= 8; i++)
                d.Add(new WPDef($"yard_bench_{i:D2}", "patio", "patio", 1, "4"));

            for (int i = 1; i <= 4; i++)
                d.Add(new WPDef($"yard_exercise_area_{i:D2}", "patio", "patio", 2, "4"));

            for (int i = 1; i <= 6; i++)
                d.Add(new WPDef($"yard_conversation_spot_{i:D2}", "patio", "patio", 2, "4"));

            for (int i = 1; i <= 2; i++)
                d.Add(new WPDef($"yard_card_table_{i:D2}", "patio", "patio", 4, "4"));

            for (int i = 1; i <= 6; i++)
                d.Add(new WPDef($"yard_wall_lean_{i:D2}", "patio", "patio", 1, "4"));

            d.Add(new WPDef("yard_ball_spot", "patio", "patio", 2, "4"));

            // ── Fases 7 y 9: Celdas ──────────────────────────────────────────
            for (int cell = 0; cell <= 9; cell++)
            {
                string c = cell.ToString("D2");
                d.Add(new WPDef($"cell_{c}_bed_01",    "celdas", "", 1, "7", "9"));
                d.Add(new WPDef($"cell_{c}_bed_02",    "celdas", "", 1, "7", "9"));
                d.Add(new WPDef($"cell_{c}_desk_01",   "celdas", "", 1, "7"));
                d.Add(new WPDef($"cell_{c}_window_01", "celdas", "", 1, "7"));
            }

            return d;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static void AppendEntry(SerializedProperty list, WPDef def)
        {
            list.arraySize++;
            var elem = list.GetArrayElementAtIndex(list.arraySize - 1);

            elem.FindPropertyRelative("waypointId").stringValue = def.id;
            elem.FindPropertyRelative("zone").stringValue       = def.zone;
            elem.FindPropertyRelative("subZone").stringValue    = def.subZone;
            elem.FindPropertyRelative("maxOccupants").intValue  = def.cap;
            elem.FindPropertyRelative("isExclusive").boolValue  = def.cap == 1;
            // waypointObject left null — user drags scene GameObject in Inspector

            var phases = elem.FindPropertyRelative("validPhases");
            phases.arraySize = def.phases.Length;
            for (int i = 0; i < def.phases.Length; i++)
                phases.GetArrayElementAtIndex(i).stringValue = def.phases[i];
        }
    }
}
