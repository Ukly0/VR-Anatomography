# Anatomy Prefab Setup

Use `Tools > Anatomy > Build Skull Prefabs` in Unity to generate prefabs from the Anatomography/BodyParts3D skull `.obj` files.

For any new BodyParts3D folder added under `Assets/External Assets/Medicine`, select the folder in Unity's Project window and run `Tools > Anatomy > Build Selected BodyParts3D Folder`. The builder expects the same source layout used by the skull and rib cage folders:

```text
BP..._partof_FMA..._Region_name
  FJ..._BP..._FMA..._Part name.obj
  FJ..._BP..._FMA..._Part name.obj.meta
```

The selected-folder builder creates part prefabs under `Assets/Anatomy/Prefabs/Parts/Skeletal/<Region name>` and a root prefab under `Assets/Anatomy/Prefabs/Layers`.

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

The setup tool:

- adds `AnatomyBoneSocketController` to the selected skull,
- creates one socket per `AnatomyPart`,
- adds grab/collider setup to each skull bone,
- disables the incorrect socket on the whole skull object,
- adds XR UI raycasters to world-space canvases if they are missing.

At runtime the skull behaves in two modes:

- assembled: only the whole skull is grabbable,
- separated: each bone becomes grabbable and its socket appears at the exploded pose so the bone can be taken out and placed back into its own socket.
