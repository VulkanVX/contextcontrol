# Project directory for Context Control

Project root: D:\projects\vulkanas
Detected profile: vulkanvx

## Prompt

Use the standing Context Control agent instructions for the full workflow. This export is only the project map/navigation layer.

For the user's request, return only the smallest safe file/function list needed for cc.ps1. One path per line, relative to project root, ending with END.

File-list example:

```	ext
src/core/engine/Engine.cpp
include/core/engine/Engine.h
src/rendering/lighting/ShadowSystemUpdate.cpp
include/rendering/lighting/ShadowSystem.h
shaders/common/shadow_sampling.glsl
END
```

Function examples accepted by upgraded cc.ps1:

```	ext
FUNCTION src/world/World.cpp :: World::markTextureMaterialsDirty
FUNCTION src/world/World*.cpp :: World::beginTerrainEditVisualTracking
FUNC: beginTerrainEditVisualTracking
SYMBOL: TerrainEditDiag
END
```

Map-stage reminders:
- Do not solve yet; request context only.
- Prefer narrow subsystem files/functions over giant exports.
- Function syntax must be exactly: FUNCTION src/path/File.cpp :: SymbolName.
- Wildcard FUNCTION paths are supported for split implementation families, e.g. FUNCTION src/world/World*.cpp :: World::foo, but prefer exact files when known.
- Use FUNC: SymbolName only when the owning file is unknown and you want cc.ps1 to search and extract function bodies.
- Use SYMBOL: SymbolName only for non-function declarations/types/constants because it exports whole matching files.
- cc.ps1 now auto-adds matching headers for .cpp files and direct GLSL #include files, so do not list obvious duplicates unless the specific header/include is central to the change.
- Include CMakeLists.txt only when adding/removing C++ source files or build config.
- Never request build/, vcpkg_installed/, vendor/external/third_party/, generated caches, binaries, or unrelated modules.

## Project tree

```	ext
vulkanas/
├── assets/
│   ├── fonts/
│   │   ├── inter.zip
│   │   ├── Inter-Medium.ttf
│   │   └── Inter-Regular.ttf
│   ├── img/
│   │   ├── cursor16x16.aseprite
│   │   └── vulkanvx.ico
│   └── sky/
│       ├── moon_16.sprite
│       ├── moon_32.sprite
│       ├── sun_16.sprite
│       └── sun_32.sprite
├── include/
│   ├── core/
│   │   ├── engine/
│   │   │   ├── Engine.h
│   │   │   └── EngineTypes.h
│   │   ├── CodeRebuildService.h
│   │   ├── CommonMath.h
│   │   ├── EngineImGui.h
│   │   ├── GameplayWindow.h
│   │   ├── Jobs.h
│   │   ├── TimeManager.h
│   │   └── WindowIcon.h
│   ├── input/
│   │   ├── CameraController.h
│   │   └── InputManager.h
│   ├── physics/
│   │   └── PhysicsWorld.h
│   ├── player/
│   │   ├── PlayerCamera.h
│   │   └── PlayerController.h
│   ├── rendering/
│   │   ├── common/
│   │   │   ├── Mesh.h
│   │   │   ├── ParallelCommandRecorder.h
│   │   │   ├── Renderer.h
│   │   │   └── VulkanHelpers.h
│   │   ├── culling/
│   │   │   ├── GPUCullingSystem.h
│   │   │   ├── GPUCullingSystem.h.gptbak.20260503_124406
│   │   │   └── HiZPyramid.h
│   │   ├── hotreload/
│   │   │   ├── ShaderCompiler.h
│   │   │   └── ShaderHotReloadService.h
│   │   ├── lighting/
│   │   │   ├── AOSettings.h
│   │   │   ├── ClusteredLightingSystem.h
│   │   │   ├── LightGlowSystem.h
│   │   │   ├── LightingSettings.h
│   │   │   ├── LightPulsePreset.h
│   │   │   ├── ShadowSystem.h
│   │   │   └── SkyPresets.h
│   │   ├── postprocess/
│   │   │   └── RetroPixelPassSystem.h
│   │   ├── sky/
│   │   │   ├── CelestialSystem.h
│   │   │   ├── CloudSystem.h
│   │   │   ├── SkySystem.h
│   │   │   └── StarSystem.h
│   │   └── tjunctionfix/
│   │       └── TJunctionFixSystem.h
│   ├── svo/
│   │   ├── SparseVoxelOctree.h
│   │   ├── SVOBuilder.h
│   │   └── SVOSerializer.h
│   ├── ui/
│   │   ├── cursor/
│   │   │   └── CursorManager.h
│   │   ├── debug_menu/
│   │   │   ├── base/
│   │   │   │   ├── DebugMenuWindows.h
│   │   │   │   └── DebugWindowBase.h
│   │   │   ├── gameplay/
│   │   │   │   ├── ControlsWindow.h
│   │   │   │   ├── CursorPlaceTool.h
│   │   │   │   ├── CursorSettingsWindow.h
│   │   │   │   └── TimeManagerWindow.h
│   │   │   ├── profiling/
│   │   │   │   ├── DebugControlPanel.h
│   │   │   │   ├── FPSProfilerWindow.h
│   │   │   │   ├── TerminalOutputWindow.h
│   │   │   │   └── WorkerThreadsWindow.h
│   │   │   ├── rendering/
│   │   │   │   ├── AOSettingsWindow.h
│   │   │   │   ├── CloudDebugWindow.h
│   │   │   │   ├── DCCMAOSettingsWindow.h
│   │   │   │   ├── DirectionalShadowWindow.h
│   │   │   │   ├── HiZDebugWindow.h
│   │   │   │   ├── LightingSettingsWindow.h
│   │   │   │   ├── PixelPassWindow.h
│   │   │   │   ├── RenderSettingsWindow.h
│   │   │   │   ├── ShaderHotReloadWindow.h
│   │   │   │   └── SkyEnclosureWindow.h
│   │   │   ├── world/
│   │   │   │   ├── ChunkDebugWindow.h
│   │   │   │   ├── ChunkHolesWindow.h
│   │   │   │   ├── ChunkMinimapWindow.h
│   │   │   │   ├── ChunkVramWindow.h
│   │   │   │   ├── MinimapCullingReadback.h
│   │   │   │   ├── ObjectManagerWindow.h
│   │   │   │   ├── TerrainEditTool.h
│   │   │   │   └── TexturePaintTool.h
│   │   │   ├── IconManagerForDebug.h
│   │   │   └── RenderSettingsWindow.h
│   │   ├── style/
│   │   │   ├── EngineTheme.h
│   │   │   └── UIAnimator.h
│   │   ├── widgets/
│   │   │   └── CompassSphereWidget.h
│   │   ├── EngineInterface.h
│   │   ├── ImGuiAutoSize.h
│   │   └── InGameDebug.h
│   ├── vulkan/
│   │   ├── Buffers.h
│   │   ├── BufferSuballocator.h
│   │   ├── FrameGraph.h
│   │   ├── FramePassTypes.h
│   │   ├── Pipeline.h
│   │   ├── Swapchain.h
│   │   ├── Sync.h
│   │   ├── UploadArena.h
│   │   └── VulkanContext.h
│   ├── world/
│   │   ├── chunks/
│   │   │   ├── core/
│   │   │   │   ├── Chunk.h
│   │   │   │   ├── ChunkJobs.h
│   │   │   │   ├── ChunkLifecycleManager.h
│   │   │   │   ├── ChunkLODSystem.h
│   │   │   │   ├── ChunkManager.h
│   │   │   │   └── ChunkManagerTypes.h
│   │   │   ├── physics/
│   │   │   │   ├── ChunkCollisionSystem.h
│   │   │   │   └── CollisionCache.h
│   │   │   └── streaming/
│   │   │       ├── ChunkRenderSystem.h
│   │   │       └── ChunkUploadSystem.h
│   │   ├── config/
│   │   │   ├── MapConfig.h
│   │   │   ├── ObjectManager.h
│   │   │   └── WorldConfig.h
│   │   ├── edit/
│   │   │   ├── HeightmapBaseSampler.h
│   │   │   ├── TerrainEditDCCMMesher.h
│   │   │   ├── TerrainEditMesher.h
│   │   │   ├── TerrainEditOverlayStore.h
│   │   │   ├── TerrainEditRemeshScheduler.h
│   │   │   ├── TerrainEditTypes.h
│   │   │   ├── TerrainFieldSource.h
│   │   │   ├── TextureOverlayStore.h
│   │   │   ├── TextureOverlayStore.h.gptbak.20260503_152500
│   │   │   ├── TextureOverlayStore.h.gptbak.20260503_153527
│   │   │   └── VoxelBaseSampler.h
│   │   ├── vxm/
│   │   │   └── VxmImport.h
│   │   ├── ChunkHoleTracker.h
│   │   ├── TerrainFileLoader.h
│   │   ├── World.h
│   │   ├── WorldDiagnostics.h
│   │   └── WorldTypes.h
│   └── pch.h
├── maps/
│   ├── snapshot/
│   │   └── snapshot.meta
│   ├── snapshot_20260430_122358/
│   │   └── snapshot.meta
│   ├── snapshot_20260430_183142/
│   │   └── snapshot.meta
│   ├── heightmap.csv
│   └── heightmap.hbin
├── shaders/
│   ├── common/
│   │   ├── clustered_lighting.glsl
│   │   ├── dither_utils.glsl
│   │   ├── shadow_sampling.glsl
│   │   └── sky_enclosure.glsl
│   ├── culling/
│   │   ├── depth_reduce.comp
│   │   ├── frustum_cull.comp
│   │   ├── frustum_cull.comp.gptbak.20260503_124408
│   │   ├── frustum_cull.comp.gptbak.20260503_220727
│   │   ├── frustum_dispatch.comp
│   │   └── frustum_filter.comp
│   ├── lighting/
│   │   ├── light_glow.frag
│   │   └── light_glow.vert
│   ├── pixelpass/
│   │   ├── pixel_pass.frag
│   │   └── pixel_pass.vert
│   ├── shadow/
│   │   ├── directional_shadow_depth.frag
│   │   ├── point_shadow_depth.frag
│   │   └── point_shadow_terrain.vert
│   ├── sky/
│   │   ├── celestial.frag
│   │   ├── celestial.vert
│   │   ├── cloud.frag
│   │   ├── cloud.vert
│   │   ├── sky.frag
│   │   ├── sky.vert
│   │   ├── star.frag
│   │   └── star.vert
│   ├── terrain/
│   │   ├── backupAOupdate.frag
│   │   ├── cube.frag
│   │   ├── cube.vert
│   │   ├── cube_debug.frag
│   │   ├── cube_debug.vert
│   │   ├── cube_zonly.vert
│   │   ├── dccm_terrain.frag
│   │   └── dccm_terrain.vert
│   └── tjunctionfix/
│       ├── tjunction_fix.frag
│       └── tjunction_fix.vert
├── src/
│   ├── core/
│   │   ├── engine/
│   │   │   ├── Engine.cpp
│   │   │   ├── EngineCleanup.cpp
│   │   │   ├── EngineCommandBuffer.cpp
│   │   │   ├── EngineDebugWiring.cpp
│   │   │   ├── EngineDepthPrePass.cpp
│   │   │   ├── EngineGameplayRendering.cpp
│   │   │   ├── EngineRenderLoop.cpp
│   │   │   ├── EngineSettingsPersistence.cpp
│   │   │   ├── EngineShaderHotReload.cpp
│   │   │   ├── EngineShadowPass.cpp
│   │   │   ├── EngineSubsystemInit.cpp
│   │   │   ├── EngineSubsystemInit.cpp.gptbak.20260503_004542
│   │   │   ├── EngineSubsystemInit.cpp.gptbak.20260503_124409
│   │   │   ├── EngineTimestamps.cpp
│   │   │   ├── EngineTimestamps.cpp.gptbak.20260503_220727
│   │   │   └── EngineVulkanInit.cpp
│   │   ├── CodeRebuildService.cpp
│   │   ├── EngineImGui.cpp
│   │   ├── GameplayWindow.cpp
│   │   ├── Jobs.cpp
│   │   ├── TimeManager.cpp
│   │   └── WindowIcon.cpp
│   ├── input/
│   │   ├── CameraController.cpp
│   │   └── InputManager.cpp
│   ├── physics/
│   │   └── PhysicsWorld.cpp
│   ├── player/
│   │   ├── PlayerCamera.cpp
│   │   └── PlayerController.cpp
│   ├── rendering/
│   │   ├── common/
│   │   │   ├── Mesh.cpp
│   │   │   ├── ParallelCommandRecorder.cpp
│   │   │   ├── Renderer.cpp
│   │   │   └── VulkanHelpers.cpp
│   │   ├── culling/
│   │   │   ├── GPUCullingReadback.cpp
│   │   │   ├── GPUCullingSlots.cpp
│   │   │   ├── GPUCullingSystem.cpp
│   │   │   ├── HiZPyramid.cpp
│   │   │   ├── HiZPyramidDiagnostics.cpp
│   │   │   └── HiZPyramidResources.cpp
│   │   ├── hotreload/
│   │   │   ├── ShaderCompiler.cpp
│   │   │   └── ShaderHotReloadService.cpp
│   │   ├── lighting/
│   │   │   ├── ClusteredLightingSystem.cpp
│   │   │   ├── LightGlowSystem.cpp
│   │   │   ├── LightingSettings.cpp
│   │   │   ├── ShadowDiagnostics.cpp
│   │   │   ├── ShadowMapRendering.cpp
│   │   │   ├── ShadowSystem.cpp
│   │   │   ├── ShadowSystemResources.cpp
│   │   │   └── ShadowSystemUpdate.cpp
│   │   ├── postprocess/
│   │   │   └── RetroPixelPassSystem.cpp
│   │   ├── sky/
│   │   │   ├── CelestialSystem.cpp
│   │   │   ├── CloudAnimation.cpp
│   │   │   ├── CloudFormation.cpp
│   │   │   ├── CloudSystem.cpp
│   │   │   ├── CloudWeather.cpp
│   │   │   ├── SkySystem.cpp
│   │   │   └── StarSystem.cpp
│   │   └── tjunctionfix/
│   │       └── TJunctionFixSystem.cpp
│   ├── svo/
│   │   ├── SparseVoxelOctree.cpp
│   │   ├── SVOBuilder.cpp
│   │   └── SVOSerializer.cpp
│   ├── ui/
│   │   ├── cursor/
│   │   │   └── CursorManager.cpp
│   │   ├── debug_menu/
│   │   │   ├── gameplay/
│   │   │   │   ├── ControlsWindow.cpp
│   │   │   │   ├── CursorPlaceTool.cpp
│   │   │   │   ├── CursorSettingsWindow.cpp
│   │   │   │   └── TimeManagerWindow.cpp
│   │   │   ├── profiling/
│   │   │   │   ├── DebugControlPanel.cpp
│   │   │   │   ├── FPSProfilerWindow.cpp
│   │   │   │   ├── TerminalOutputWindow.cpp
│   │   │   │   └── WorkerThreadsWindow.cpp
│   │   │   ├── rendering/
│   │   │   │   ├── AOSettingsWindow.cpp
│   │   │   │   ├── CloudDebugWindow.cpp
│   │   │   │   ├── DCCMAOSettingsWindow.cpp
│   │   │   │   ├── DirectionalShadowWindow.cpp
│   │   │   │   ├── GlobalLightDebugWindow.cpp
│   │   │   │   ├── HiZDebugWindow.cpp
│   │   │   │   ├── LightingSettingsWindow.cpp
│   │   │   │   ├── PixelPassWindow.cpp
│   │   │   │   ├── RenderSettingsWindow.cpp
│   │   │   │   ├── ShaderHotReloadWindow.cpp
│   │   │   │   └── SkyEnclosureWindow.cpp
│   │   │   ├── world/
│   │   │   │   ├── ChunkDebugWindow.cpp
│   │   │   │   ├── ChunkHolesWindow.cpp
│   │   │   │   ├── ChunkMinimapCullingOverlay.cpp
│   │   │   │   ├── ChunkMinimapWindow.cpp
│   │   │   │   ├── ChunkVramWindow.cpp
│   │   │   │   ├── ChunkVramWindow_Internal.h
│   │   │   │   ├── ChunkVramWindow_Text.cpp
│   │   │   │   ├── MinimapCullingReadback.cpp
│   │   │   │   ├── ObjectManagerWindow.cpp
│   │   │   │   ├── TerrainEditTool.cpp
│   │   │   │   ├── TexturePaintTool.cpp
│   │   │   │   ├── TexturePaintTool.cpp.gptbak.20260503_005128
│   │   │   │   ├── TexturePaintTool.cpp.gptbak.20260503_010736
│   │   │   │   ├── TexturePaintTool.cpp.gptbak.20260503_130543
│   │   │   │   ├── TexturePaintTool.cpp.gptbak.20260503_150217
│   │   │   │   ├── TexturePaintTool.cpp.gptbak.20260503_152502
│   │   │   │   ├── TexturePaintTool.cpp.gptbak.20260503_214910
│   │   │   │   └── TexturePaintTool.cpp.gptbak.20260503_221426
│   │   │   ├── IconManagerForDebug.cpp
│   │   │   └── RenderSettingsWindow.cpp
│   │   ├── style/
│   │   │   ├── EngineTheme.cpp
│   │   │   └── UIAnimator.cpp
│   │   ├── widgets/
│   │   │   └── CompassSphereWidget.cpp
│   │   ├── EngineInterface.cpp
│   │   ├── EngineInterfaceGameplay.cpp
│   │   ├── EngineInterfaceLayout.cpp
│   │   └── InGameDebug.cpp
│   ├── vulkan/
│   │   ├── Buffers.cpp
│   │   ├── BufferSuballocator.cpp
│   │   ├── FrameGraph.cpp
│   │   ├── Pipeline.cpp
│   │   ├── Swapchain.cpp
│   │   ├── Sync.cpp
│   │   ├── UploadArena.cpp
│   │   └── VulkanContext.cpp
│   ├── world/
│   │   ├── chunks/
│   │   │   ├── core/
│   │   │   │   ├── ChunkJobs.cpp
│   │   │   │   ├── ChunkLifecycleManager.cpp
│   │   │   │   ├── ChunkLODSystem.cpp
│   │   │   │   ├── ChunkManager.cpp
│   │   │   │   ├── ChunkManagerBatches.cpp
│   │   │   │   └── ChunkManagerRings.cpp
│   │   │   ├── physics/
│   │   │   │   ├── ChunkCollisionSystem.cpp
│   │   │   │   └── CollisionCache.cpp
│   │   │   └── streaming/
│   │   │       ├── ChunkRenderSystem.cpp
│   │   │       └── ChunkUploadSystem.cpp
│   │   ├── config/
│   │   │   └── WorldConfig.cpp
│   │   ├── edit/
│   │   │   ├── HeightmapBaseSampler.cpp
│   │   │   ├── TerrainEditDCCMMesher.cpp
│   │   │   ├── TerrainEditMesher.cpp
│   │   │   ├── TerrainEditMesher.cpp.gptbak.20260503_221426
│   │   │   ├── TerrainEditOverlayStore.cpp
│   │   │   ├── TerrainEditOverlayStore_IO.cpp
│   │   │   ├── TerrainEditRemeshScheduler.cpp
│   │   │   ├── TerrainFieldSource.cpp
│   │   │   ├── TextureOverlayStore.cpp
│   │   │   └── VoxelBaseSampler.cpp
│   │   ├── vxm/
│   │   │   └── VxmImport.cpp
│   │   ├── ChunkHoleTracker.cpp
│   │   ├── TerrainFileLoader.cpp
│   │   ├── World.cpp
│   │   ├── WorldChunkCRUD.cpp
│   │   ├── WorldChunkReset.cpp
│   │   ├── WorldDebugMetrics.cpp
│   │   ├── WorldLODSwaps.cpp
│   │   ├── WorldLODTransitions.cpp
│   │   ├── WorldRendering.cpp
│   │   ├── WorldSnapshots.cpp
│   │   ├── WorldTerrainEditCollision.cpp
│   │   ├── WorldUpdate.cpp
│   │   ├── WorldUpdateFinalize.cpp
│   │   ├── WorldUpdateLODScan.cpp
│   │   └── WorldUpdateMeshing.cpp
│   ├── CMakeLists.txt
│   └── main.cpp
├── tools/
│   ├── CMakeLists.txt
│   ├── convert_heightmap_to_meshes.cpp
│   ├── convert_heightmap_to_svo.cpp
│   ├── fix_dccm_gaps.cpp
│   ├── generate_collision_cache.cpp
│   ├── generate_test_terrain.cpp
│   └── IndexOptimizer.h
└── CMakeLists.txt
```
