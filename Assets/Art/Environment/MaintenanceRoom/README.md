# Maintenance Room

Drag **Prefabs/MaintenanceRoom.prefab** into a scene for the complete 5 x 4 x 3 m room. Open **Scenes/MaintenanceRoom_Preview.unity** for the isolated interior preview.

**Prefabs/Modules** contains 20 reusable modules. The assembled room has 105 module instances, 17,892 triangles, 16 shared URP materials and 70 primitive colliders. One Unity unit is one meter; prefab roots have identity scale. Walls/ceiling are separate groups. Wall-module fronts face local -Z.

The table is empty. No ladder, floor marking, electrical box, sign, equipment or PPE is included. Door and cabinet leaves have hinge pivots; no animation or gameplay is attached. Cabinet collision is a single static body box.

**Tools > Safety > Maintenance Room > Validate Assets** checks the saved delivery. **Rebuild Assets** regenerates this folder's prefabs, materials and preview scene. Save your current scene first; keep your customizations in prefab variants.

Editable Blender source, FBXs, six previews, measured validation reports and full usage/rebuild notes are in the workspace's **Art/Models/MaintenanceRoom** folder.
