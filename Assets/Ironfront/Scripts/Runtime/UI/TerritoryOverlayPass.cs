using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Ironfront.UI
{
    /// <summary>
    /// Draws the construction territory overlay (Milestone 010) <b>after</b> the full-screen fog-of-war pass, so the
    /// player's own territory, the deployment preview and known enemy zones stay readable over unexplored (black) and
    /// dimmed ground. It still tests against scene depth, so structures and units in front hide it, and it runs before
    /// post-processing, so it is tone-mapped like the rest of the scene. The pass is queued from script for game cameras
    /// only while <see cref="TerritoryOverlay"/> is shown; no renderer asset changes are needed.
    /// </summary>
    public sealed class TerritoryOverlayPass : ScriptableRenderPass
    {
        private static TerritoryOverlayPass s_pass;
        private static readonly List<(Mesh mesh, Material material)> s_draws = new List<(Mesh, Material)>();
        private static bool s_hooked;

        private sealed class PassData
        {
            public readonly List<(Mesh mesh, Material material)> Draws = new List<(Mesh, Material)>();
        }

        private TerritoryOverlayPass()
        {
            // The fog of war is a full-screen pass at BeforeRenderingPostProcessing: draw right after it.
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing + 1;
        }

        /// <summary>Sets what to draw this frame (an empty list draws nothing).</summary>
        public static void Submit(List<(Mesh mesh, Material material)> draws)
        {
            s_draws.Clear();
            if (draws != null) s_draws.AddRange(draws);
            if (s_hooked || s_draws.Count == 0) return;
            s_pass ??= new TerritoryOverlayPass();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            s_hooked = true;
        }

        public static void Clear() => s_draws.Clear();

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (s_draws.Count == 0 || camera.cameraType != CameraType.Game) return;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(s_pass);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (s_draws.Count == 0) return;
            var resources = frameData.Get<UniversalResourceData>();
            using var builder = renderGraph.AddRasterRenderPass<PassData>("Ironfront Territory Overlay", out var passData);
            passData.Draws.Clear();
            passData.Draws.AddRange(s_draws);
            builder.SetRenderAttachment(resources.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
            {
                foreach (var (mesh, material) in d.Draws)
                    if (mesh != null && material != null && mesh.vertexCount > 0)
                        ctx.cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            });
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (s_hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            s_hooked = false;
            s_draws.Clear();
        }
    }
}
