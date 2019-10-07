# DOTS-ProceduralTileStreamingSample

Illustrates how to stream in & out tiles of prefab instances procedurally

The basic functionality works, performance is at a reasonable starting point @ 60 FPS on a Macbook Pro.
* 30.000 instances get continously created & destroyed in tiles 
  * Tree (static convex collider + 2 material renderer), 
  * Rock (static convex collider + 1 material renderer), 
  * Dynamic rock falling (dynamic physics body with convex collider + 1 material renderer)


The functionality is optimizable. I expect that we can probably make this run ~10x faster with some targeted optimizations.

# Known Issues
* Unity.Entities lacks a good way of static optimizing entities when they will be instantiated at runtime (This is required for Dots Terrain and is a requirement for Entities 1.0)
  * Currently all instances have a transform hierarchy & are paying for it at runtime... when it could really be a simple LocalToWorld, which gets offset during instantiation.
  * All renderers go through the dynamic path, when they could be taking the static frozen fast path we used on megacity (~5ms is spent on this alone...)
* Scale is not supported since Unity.Physics does not yet support runtime uniform scale (Unless we want to clone the collision mesh...)
