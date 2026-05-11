# Anatomy Prefab Setup

Use `Tools > Anatomy > Build Skull Prefabs` in Unity to generate prefabs from the Anatomography/BodyParts3D skull `.obj` files.

For any new BodyParts3D folder added under `Assets/External Assets/Medicine`, select the folder in Unity's Project window and run `Tools > Anatomy > Build Selected BodyParts3D Folder`. The builder expects the same source layout used by the skull and rib cage folders:

```text
BP..._partof_FMA..._Region_name
  FJ..._BP..._FMA..._Part name.obj
  FJ..._BP..._FMA..._Part name.obj.meta
```

The selected-folder builder creates part prefabs under `Assets/Anatomy/Prefabs/Parts/Skeletal/<Region name>` and a root prefab under `Assets/Anatomy/Prefabs/Layers`.

To create a standalone grabbable parent assembly from a folder of part prefabs, select a folder under `Assets/Anatomy/Prefabs/Parts/Skeletal/...` and run `Tools > Anatomy > Create Assembly From Selected Parts Folder`. The tool adds every direct `AnatomyPart` prefab in that folder under the scene `-- Anatomy --` root with this hierarchy:

```text
<FolderName>Assambly
  <FolderName>GrabHandle
    GrabAttach
    VisualRoot
      Canvas
      PF_<PartNameA>
      PF_<PartNameB>
      ...
```

Each selected child prefab keeps its original local coordinates inside `VisualRoot`, and `VisualRoot` keeps the same anatomical transform pattern used by the existing skull and rib assemblies. The generated `GrabAttach`, grab collider, and socket are placed from the visible renderer bounds of the full folder assembly, so assets whose mesh data is offset to its anatomical body position still grab and snap from the visible anatomy instead of from the prefab origin. The whole-assembly socket hides the world-space canvas while the assembly is docked, matching the skull and rib button behavior.

Whole assemblies get an automatically selected XR interaction layer bit for their generated `GrabHandle` and matching whole socket, so a socket from one visible assembly does not accept another visible assembly. Child parts still use the shared part layer and `AnatomySocketMatchFilter` to avoid exhausting interaction layer bits. The generated whole socket also includes a `SocketGhost` clone of the visible assembly; it stays hidden by default and only appears while the whole assembly is being grabbed near its socket, then hides again when the assembly is docked or moved away.

The generated structure is:

```text
Assets/Anatomy
  Materials
    MAT_Bone.mat
  Prefabs
    Layers
      PF_AnatomyRoot_Skull.prefab
      PF_AnatomyRoot_Rib_cage.prefab
    Parts
      Skeletal
        Skull
          PF_Frontal bone.prefab
          PF_Mandible.prefab
          ...
        Rib cage
          PF_Body of sternum.prefab
          PF_Left first rib.prefab
          ...
```

`PF_AnatomyRoot_Skull.prefab` keeps the original BodyParts3D coordinates, so all skull pieces stay aligned. Move, rotate, or scale this parent prefab instead of repositioning individual pieces.

Each part prefab has an `AnatomyPart` component with its display name, FMA id, source asset path, and source bounds. The layer prefab has an `AnatomyLayer` component so it can be toggled from UI later.

## Exploded anatomy interaction

Add `AnatomyExploder` to any parent object that contains `AnatomyPart` children. For the skull, add it to the `Skull` child inside `PF_AnatomyRoot_Skull`; for future body regions, add it to the corresponding region parent.

Connect a UI or XR button to one of these public methods:

- `ToggleSeparated()` to switch between assembled and separated states.
- `Separate()` to only move parts outward.
- `Assemble()` to return parts to their original positions.

By default, the component calculates a center point for the selected anatomy group and moves each part away from that center, so the same behavior can be reused for skull, torso, limbs, organs, or any future compact group made of `AnatomyPart` children.

For long anatomy groups that mainly grow along one axis, such as `Set of all vertebrae`, set `Separation Mode` to `Local Direction`. This keeps the existing center behavior available for prefabs where it looks good, while allowing vertebrae or similar prefabs to spread in sequence along `Local Separation Direction` instead of splitting from the middle of the group. In this mode, `Separation Distance` is the spacing step between adjacent parts.

## Bone sockets for the skull

Use `Tools > Anatomy > Setup Selected Skull Bone Sockets` after selecting the `Skull` object in the scene.

For other anatomy groups, such as `Rib cage` or `Set of all vertebrae`, select the object that has or contains the `AnatomyExploder` and use `Tools > Anatomy > Setup Selected Anatomy Sockets`.

The setup tool:

- adds `AnatomyBoneSocketController` to the selected skull,
- creates one socket per `AnatomyPart`,
- adds grab/collider setup to each skull bone,
- disables the incorrect socket on the whole skull object,
- adds XR UI raycasters to world-space canvases if they are missing.
- uses one shared interaction layer by default and relies on `AnatomySocketMatchFilter` so each part only fits in its own socket. Use `UniquePerPart` only for legacy setups that intentionally need one interaction layer per part.

At runtime the skull behaves in two modes:

- assembled: only the whole skull is grabbable,
- separated: each bone becomes grabbable and its socket appears at the exploded pose so the bone can be taken out and placed back into its own socket.
