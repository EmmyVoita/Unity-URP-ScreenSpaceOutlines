using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using System.Numerics;
using Unity.VisualScripting;


namespace SSOutlines
{
    public class ScreenSpaceOutlines : ScriptableRendererFeature
    {

        public enum InjectionPoint
        {
            /// <summary>
            /// Inject a full screen pass before transparents are rendered
            /// </summary>
            BeforeRenderingTransparents = RenderPassEvent.BeforeRenderingTransparents,
            /// <summary>
            /// Inject a full screen pass before post processing is rendered
            /// </summary>
            BeforeRenderingPostProcessing = RenderPassEvent.BeforeRenderingPostProcessing,
            /// <summary>
            /// Inject a full screen pass after post processing is rendered
            /// </summary>
            AfterRenderingPostProcessing = RenderPassEvent.AfterRenderingPostProcessing
        }



        [System.Serializable]
        private class ViewSpaceNormalsTextureSettings 
        {
            public Color backgroundColor = Color.black;
            public int depthBufferBits = 16;
            public RenderTextureFormat colorFormat = RenderTextureFormat.ARGBFloat;
            public FilterMode filterMode = FilterMode.Bilinear; 

            [Range(0.0f, 1.0f)]
            public float resolutionScale = 1;

            [Header("View Space Normal Texture Object Draw Settings")]
            public PerObjectData perObjectData;
            public bool enableDynamicBatching;
            public bool enableInstancing;
        }

        [System.Serializable]
        private class VelocityBufferSettings 
        {
            public Color backgroundColor = Color.black;
            public int depthBufferBits = 0;
            public RenderTextureFormat colorFormat = RenderTextureFormat.RGFloat;
            public InjectionPoint injectionPoint = InjectionPoint.BeforeRenderingTransparents;
            public ScriptableRenderPassInput requirements = ScriptableRenderPassInput.Motion;
            public FilterMode filterMode = FilterMode.Point; 
            public float resolutionScale = 1;
        }

        
        [System.Serializable]
        private class CustomDepthTextureSettings
        {
            public Color backgroundColor = Color.black;
            public int depthBufferBits = 16;
            public RenderTextureFormat colorFormat = RenderTextureFormat.RGFloat;
            public FilterMode filterMode = FilterMode.Bilinear; // Add this line

            [Range(0.0f, 1.0f)]
            public float resolutionScale = 1;
        }
        

        

        [System.Serializable]
        private class OutlineShaderMaterialSettings
        {
           
            [ColorUsage(true, true)] public Color OutlineColor = Color.black;
            public float OutlineScale = 2.5f;

            [Header("Depth Based Outlines")]    
            public float DepthThreshold = 1.5f;


            [Header("Roberts Cross Depth")]
            public float RobertsCrossMultiplier = 100.0f;
            public bool useNMS = true;

            [Tooltip("Outline scale factor for the Non-Maximum Suppression (NMS). Higher values mean that the NMS will look at a larger area for suppression, which results in the outline being closer to the main outline scale value.")]
            public float NMS_OutlineScale = 0.7f;
        

            [Header("Depth Based Outlines Artifact Correction")]   
            public float SteepAngleThreshold = 2.5f;
            public float SteepAngleMultiplier = 25.0f;

            
            [Header("Normal Based Outlines")]
            public float NormalThreshold = 0.5f;
            public bool scaleNormalThresholdWithDistance = true;

            
            [Header("Distance Based Outline Scale")]
            public float DistancePow = 1.05f;
            
        }

        
        private class CustomDepthTexturePass : ScriptableRenderPass
        {
            private TemporalReprojection temporalReprojection;
            private CustomDepthTextureSettings depthTextureSettings;
            private readonly List<ShaderTagId> shaderTagIdList;
            private RTHandle depthTex;
            private readonly Material depthMaterial;
            private FilteringSettings filteringSettings;

            public CustomDepthTexturePass(RenderPassEvent renderPassEvent, CustomDepthTextureSettings settings, LayerMask layerMask, TemporalReprojection temporalReprojection)
            {
                shaderTagIdList = new List<ShaderTagId> { new ShaderTagId("DepthOnly")};

                this.renderPassEvent = renderPassEvent;
                this.temporalReprojection = temporalReprojection;
                depthTex = RTHandles.Alloc("_CustomDepthTexture", name: "_CustomDepthTexture");
                depthTextureSettings = settings;


                this.depthMaterial = new Material(Shader.Find("Shader Graphs/DepthShader"));
                filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);
            }
            

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                if(depthTextureSettings == null)
                {
                    Debug.LogError("No depth texture settings");
                    return;
                }
   
                RenderTextureDescriptor depthTextureDescriptor = cameraTextureDescriptor;
                depthTextureDescriptor.colorFormat = depthTextureSettings.colorFormat;
                depthTextureDescriptor.depthBufferBits = depthTextureSettings.depthBufferBits;

                depthTextureDescriptor.width = Mathf.RoundToInt(depthTextureDescriptor.width * depthTextureSettings.resolutionScale);
                depthTextureDescriptor.height = Mathf.RoundToInt(depthTextureDescriptor.height * depthTextureSettings.resolutionScale);

                cmd.GetTemporaryRT(Shader.PropertyToID(depthTex.name), depthTextureDescriptor, depthTextureSettings.filterMode);

                ConfigureTarget(depthTex);
                ConfigureClear(ClearFlag.All, depthTextureSettings.backgroundColor);
            }


            // executes the render pass
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if(!depthMaterial) return;

                CommandBuffer cmd = CommandBufferPool.Get();
            
                using(new UnityEngine.Rendering.ProfilingScope(cmd, new ProfilingSampler("RenderToDepthTexture")))
                {
                    
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

              
                    UnityEngine.Matrix4x4 projectionMatrix = renderingData.cameraData.camera.GetProjectionMatrix(temporalReprojection._frustumJitter.activeSample.x, temporalReprojection._frustumJitter.activeSample.y);
                    cmd.SetProjectionMatrix(projectionMatrix);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                

                    DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIdList,
                                                                            ref renderingData, 
                                                                            renderingData.cameraData.defaultOpaqueSortFlags);

                    drawingSettings.overrideMaterial = depthMaterial;


                    context.DrawRenderers(  renderingData.cullResults,
                                            ref drawingSettings, 
                                            ref filteringSettings);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);

            }


            public override void OnCameraCleanup(CommandBuffer cmd){
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(depthTex.name));
            }
        }

        

        private class ViewSpaceNormalsTexturePass : ScriptableRenderPass
        {
            private TemporalReprojection temporalReprojection;
            private ViewSpaceNormalsTextureSettings normalsTextureSettings;
            private readonly List<ShaderTagId> shaderTagIdList;
            private readonly RTHandle normals;

            private readonly Material normalsMaterial;

            private FilteringSettings filteringSettings;


            public ViewSpaceNormalsTexturePass(RenderPassEvent renderPassEvent, ViewSpaceNormalsTextureSettings settings, LayerMask outlinesLayerMask, TemporalReprojection temporalReprojection)
            {
                shaderTagIdList = new List<ShaderTagId> {   new ShaderTagId("UniversalForward"), 
                                                            new ShaderTagId("UniversalForwardOnly"), 
                                                            new ShaderTagId("LightweightForward"), 
                                                            new ShaderTagId("SRPDefaultUnlit"), };
                this.renderPassEvent = renderPassEvent;
                this.temporalReprojection = temporalReprojection;
                normals = RTHandles.Alloc("_SceneViewSpaceNormals", name: "_SceneViewSpaceNormals");
                normalsTextureSettings = settings;


                

                this.normalsMaterial =  new Material(Shader.Find("Example/ViewSpaceNormals"));
                //this.normalsMaterial =  new Material(Shader.Find("Shader Graphs/ViewSpaceNormalsShader"));

                filteringSettings = new FilteringSettings(RenderQueueRange.opaque, outlinesLayerMask);

            }

            // called before render pass
            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                // normalsTextureDescriptor setup
                RenderTextureDescriptor normalTextureDescriptor = cameraTextureDescriptor;
                normalTextureDescriptor.colorFormat = normalsTextureSettings.colorFormat;
                normalTextureDescriptor.depthBufferBits = normalsTextureSettings.depthBufferBits;

                normalTextureDescriptor.width = Mathf.RoundToInt(normalTextureDescriptor.width * normalsTextureSettings.resolutionScale);
                normalTextureDescriptor.height = Mathf.RoundToInt(normalTextureDescriptor.height * normalsTextureSettings.resolutionScale);


                cmd.GetTemporaryRT(Shader.PropertyToID(normals.name), normalTextureDescriptor, normalsTextureSettings.filterMode);

                ConfigureTarget(normals);
                ConfigureClear(ClearFlag.All, normalsTextureSettings.backgroundColor);
            }

            // executes the render pass
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                
                if(!normalsMaterial)
                {
                    //Debug.LogError("No normal material");
                    return;
                }
                

                CommandBuffer cmd = CommandBufferPool.Get();
            
                using(new UnityEngine.Rendering.ProfilingScope(cmd, new ProfilingSampler("SceneViewSpaceNormalsTextureCreation")))
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    UnityEngine.Matrix4x4 projectionMatrix = renderingData.cameraData.camera.GetProjectionMatrix(temporalReprojection._frustumJitter.activeSample.x, temporalReprojection._frustumJitter.activeSample.y);
                    cmd.SetProjectionMatrix(projectionMatrix);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();


                    DrawingSettings drawingSettings = CreateDrawingSettings(    shaderTagIdList, 
                                                                                ref renderingData,
                                                                                renderingData.cameraData.defaultOpaqueSortFlags);
                            
                    drawingSettings.overrideMaterial = normalsMaterial;



                    context.DrawRenderers(  renderingData.cullResults,
                                            ref drawingSettings, 
                                            ref filteringSettings);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(normals.name));
            }

        }

        private class VelocityBufferTexturePass : ScriptableRenderPass
        {
            private TemporalReprojection temporalReprojection;
            private VelocityBufferSettings mvTextureSettings;
            private readonly List<ShaderTagId> shaderTagIdList;
            private readonly RTHandle motionVectors;

            private readonly Material motionVectorsMaterial;

            private FilteringSettings filteringSettings;


            public VelocityBufferTexturePass(RenderPassEvent renderPassEvent, VelocityBufferSettings settings, LayerMask outlinesLayerMask, TemporalReprojection temporalReprojection)
            {
                shaderTagIdList = new List<ShaderTagId> {   new ShaderTagId("UniversalForward"), 
                                                            new ShaderTagId("UniversalForwardOnly"), 
                                                            new ShaderTagId("LightweightForward"), 
                                                            new ShaderTagId("SRPDefaultUnlit"), };
                this.renderPassEvent = renderPassEvent;
                this.temporalReprojection = temporalReprojection;

                motionVectors = RTHandles.Alloc("_VelocityBuffer", name: "_VelocityBuffer");
                mvTextureSettings = settings;


                Shader shader = Shader.Find("Shader Graphs/VelocityBuffer");
                this.motionVectorsMaterial =  new Material(shader);


                filteringSettings = new FilteringSettings(RenderQueueRange.opaque, outlinesLayerMask);
            }


            // called before render pass
            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                // normalsTextureDescriptor setup
                RenderTextureDescriptor mvTextureDescriptor = cameraTextureDescriptor;
                mvTextureDescriptor.colorFormat = mvTextureSettings.colorFormat;
                mvTextureDescriptor.depthBufferBits = mvTextureSettings.depthBufferBits;

                mvTextureDescriptor.width = Mathf.RoundToInt(mvTextureDescriptor.width * mvTextureSettings.resolutionScale);
                mvTextureDescriptor.height = Mathf.RoundToInt(mvTextureDescriptor.height * mvTextureSettings.resolutionScale);


                cmd.GetTemporaryRT(Shader.PropertyToID(motionVectors.name), mvTextureDescriptor, mvTextureSettings.filterMode);

                ConfigureTarget(motionVectors);
                ConfigureClear(ClearFlag.All, mvTextureSettings.backgroundColor);

        
            }

            // executes the render pass
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                
                if(!motionVectorsMaterial)
                {
                    //Debug.LogError("No VelocityBuffer material");
                    return;
                }
                

                CommandBuffer cmd = CommandBufferPool.Get();
            
                using(new UnityEngine.Rendering.ProfilingScope(cmd, new ProfilingSampler("VelocityBufferCreation")))
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    
                    UnityEngine.Matrix4x4 projectionMatrix = renderingData.cameraData.camera.GetProjectionMatrix(temporalReprojection._frustumJitter.activeSample.x, temporalReprojection._frustumJitter.activeSample.y);
                    cmd.SetProjectionMatrix(projectionMatrix);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    
                    /*
                    if(temporalReprojection.useTAA)
                    {
                        
                    }
                    */
                    


                    DrawingSettings drawingSettings = CreateDrawingSettings(    shaderTagIdList, 
                                                                                ref renderingData,
                                                                                renderingData.cameraData.defaultOpaqueSortFlags);
                    
                    drawingSettings.overrideMaterial = motionVectorsMaterial;



                    context.DrawRenderers(  renderingData.cullResults,
                                            ref drawingSettings, 
                                            ref filteringSettings);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            // called when the camera is finished rendering
            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(motionVectors.name));
            }

        }


        private class ScreenSpaceOutlinesPass_New : ScriptableRenderPass
        {
            private TemporalReprojection temporalReprojection;

            private readonly Material screenSpaceOutlineMaterial;
            private readonly Material normalsMaterial;

            private readonly List<ShaderTagId> shaderTagIdList;
            private OutlineShaderMaterialSettings outlineShaderMaterialSettings;
            private ViewSpaceNormalsTextureSettings viewSpaceNormalsTextureSettings;
            private FilteringSettings filteringSettings;
            private RTHandle sso;


            private RTHandle normals;
            private RendererList normalsRenderersList;

            private RenderTargetIdentifier cameraColorTarget;
            //private RenderTargetIdentifier temporaryBuffer;
            //private int temporaryBufferID = Shader.PropertyToID("_EdgeTexture");

            public ScreenSpaceOutlinesPass_New(RenderPassEvent renderPassEvent, OutlineShaderMaterialSettings outlineShaderMaterialSettings, ViewSpaceNormalsTextureSettings viewSpaceNormalsTextureSettings, LayerMask layerMask, TemporalReprojection temporalReprojection)
            {
                this.renderPassEvent = renderPassEvent;
                this.temporalReprojection = temporalReprojection;
                
                this.screenSpaceOutlineMaterial =  new Material(Shader.Find("Shader Graphs/OutlineShader_v2"));
                this.normalsMaterial = new Material(Shader.Find("Example/ViewSpaceNormals")); 
                //this.normalsMaterial = new Material(Shader.Find("Hidden/ViewSpaceNormals")); 
                
                filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);

                shaderTagIdList = new List<ShaderTagId> {
                    new ShaderTagId("UniversalForward"),
                    new ShaderTagId("UniversalForwardOnly"),
                    new ShaderTagId("LightweightForward"),
                    new ShaderTagId("SRPDefaultUnlit")
                };

                sso = RTHandles.Alloc("_EdgeTexture", name: "_EdgeTexture");
                normals = RTHandles.Alloc("_SceneViewSpaceNormals", name: "_SceneViewSpaceNormals");
    
                this.outlineShaderMaterialSettings = outlineShaderMaterialSettings;
                this.viewSpaceNormalsTextureSettings = viewSpaceNormalsTextureSettings;
                
                //filteringSettings = new FilteringSettings(RenderQueueRange.opaque, 0);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

                
                // Normal Texture
                RenderTextureDescriptor normal_textureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                normal_textureDescriptor.colorFormat = viewSpaceNormalsTextureSettings.colorFormat;
                normal_textureDescriptor.depthBufferBits = viewSpaceNormalsTextureSettings.depthBufferBits;

                //RenderingUtils.ReAllocateIfNeeded(ref normals, normal_textureDescriptor, viewSpaceNormalsTextureSettings.filterMode);
                cmd.GetTemporaryRT(Shader.PropertyToID(normals.name), normal_textureDescriptor, viewSpaceNormalsTextureSettings.filterMode);

                // Outline Texture
                RenderTextureDescriptor outline_textureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                outline_textureDescriptor.colorFormat = RenderTextureFormat.ARGBFloat;
                outline_textureDescriptor.depthBufferBits = 0;

                //RenderingUtils.ReAllocateIfNeeded(ref sso, outline_textureDescriptor, FilterMode.Bilinear);
                cmd.GetTemporaryRT(Shader.PropertyToID(sso.name), outline_textureDescriptor, FilterMode.Bilinear);

                //ConfigureTarget(normals, renderingData.cameraData.renderer.cameraDepthTargetHandle);
                //ConfigureClear(ClearFlag.Color, viewSpaceNormalsTextureSettings.backgroundColor);

                //ConfigureTarget(sso);
                //ConfigureClear(ClearFlag.All, Color.clear);
                
            }




            /*
            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                
                // Normal Texture
                RenderTextureDescriptor normal_textureDescriptor = cameraTextureDescriptor;
                normal_textureDescriptor.colorFormat = viewSpaceNormalsTextureSettings.colorFormat;
                normal_textureDescriptor.depthBufferBits = viewSpaceNormalsTextureSettings.depthBufferBits;

                RenderingUtils.ReAllocateIfNeeded(ref normals, normal_textureDescriptor, viewSpaceNormalsTextureSettings.filterMode);


                // Outline Texture
                RenderTextureDescriptor outline_textureDescriptor = cameraTextureDescriptor;
                outline_textureDescriptor.colorFormat = RenderTextureFormat.ARGBFloat;
                outline_textureDescriptor.depthBufferBits = 0;

                cmd.GetTemporaryRT(Shader.PropertyToID(sso.name), outline_textureDescriptor, FilterMode.Bilinear);

                //RenderingUtils.ReAllocateIfNeeded(ref sso, outline_textureDescriptor, FilterMode.Bilinear);


                ConfigureTarget(sso);
                ConfigureClear(ClearFlag.All, Color.clear);
                
            }
            */

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!screenSpaceOutlineMaterial || !normalsMaterial || 
                renderingData.cameraData.renderer.cameraColorTargetHandle.rt == null)
                return;
                
                //ConfigureTarget(normals, renderingData.cameraData.renderer.cameraDepthTargetHandle);
                //ConfigureClear(ClearFlag.Color, Color.clear);

                CommandBuffer cmd = CommandBufferPool.Get();

                // Set the render target for the normals pass
                cmd.SetRenderTarget(normals.nameID, renderingData.cameraData.renderer.cameraDepthTargetHandle.nameID);
                cmd.ClearRenderTarget(true, true, Color.clear);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                //context.ExecuteCommandBuffer(cmd);
                //cmd.Clear();

                //UnityEngine.Matrix4x4 projectionMatrix = renderingData.cameraData.camera.GetProjectionMatrix(temporalReprojection._frustumJitter.activeSample.x, temporalReprojection._frustumJitter.activeSample.y);
                //cmd.SetProjectionMatrix(projectionMatrix);

                
                
                
                

                // Normals
                DrawingSettings drawSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
                drawSettings.perObjectData = viewSpaceNormalsTextureSettings.perObjectData;
                drawSettings.enableDynamicBatching = viewSpaceNormalsTextureSettings.enableDynamicBatching;
                drawSettings.enableInstancing = viewSpaceNormalsTextureSettings.enableInstancing;
                drawSettings.overrideMaterial = normalsMaterial;
                
                RendererListParams normalsRenderersParams = new RendererListParams(renderingData.cullResults, drawSettings, filteringSettings);
                normalsRenderersList = context.CreateRendererList(ref normalsRenderersParams);
                
                using (new UnityEngine.Rendering.ProfilingScope(cmd, new ProfilingSampler("DrawRendererList - Normals")))
                {
                    cmd.DrawRendererList(normalsRenderersList);
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                //cmd.SetGlobalTexture(Shader.PropertyToID("_SceneViewSpaceNormals"), normals.rt);


                // Configure the render target for the screen-space outline pass
                cmd.SetRenderTarget(sso.nameID);
                cmd.ClearRenderTarget(true, true, Color.clear);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                //UnityEngine.Matrix4x4 projectionMatrix = renderingData.cameraData.camera.GetProjectionMatrix(temporalReprojection._frustumJitter.activeSample.x, temporalReprojection._frustumJitter.activeSample.y);
                //cmd.SetProjectionMatrix(projectionMatrix);
                

                screenSpaceOutlineMaterial.SetFloat("_OutlineScale", outlineShaderMaterialSettings.OutlineScale);
                screenSpaceOutlineMaterial.SetFloat("_DepthThreshold", outlineShaderMaterialSettings.DepthThreshold);
                screenSpaceOutlineMaterial.SetFloat("_RobertsCrossMultiplier", outlineShaderMaterialSettings.RobertsCrossMultiplier);
                screenSpaceOutlineMaterial.SetFloat("_NormalThreshold", outlineShaderMaterialSettings.NormalThreshold);
                screenSpaceOutlineMaterial.SetInt("_ScaleNormalThresholdWithDistance", outlineShaderMaterialSettings.scaleNormalThresholdWithDistance ? 1 : 0);
                screenSpaceOutlineMaterial.SetFloat("_SteepAngleThreshold", outlineShaderMaterialSettings.SteepAngleThreshold);
                screenSpaceOutlineMaterial.SetFloat("_SteepAngleMultiplier", outlineShaderMaterialSettings.SteepAngleMultiplier);
                screenSpaceOutlineMaterial.SetFloat("_DistancePow", outlineShaderMaterialSettings.DistancePow);
                screenSpaceOutlineMaterial.SetFloat("_NMSAmount", outlineShaderMaterialSettings.NMS_OutlineScale);
                EffectBase.EnsureKeyword(screenSpaceOutlineMaterial, "USE_NMS", outlineShaderMaterialSettings.useNMS);
                
                //ConfigureTarget(sso);
                //ConfigureClear(ClearFlag.All, Color.clear);
                
                using(new UnityEngine.Rendering.ProfilingScope(cmd, new ProfilingSampler("ScreenSpaceOutlines")))
                {
                    cmd.DrawMesh(RenderingUtils.fullscreenMesh, UnityEngine.Matrix4x4.identity, screenSpaceOutlineMaterial);
                }

                //cmd.SetGlobalTexture(Shader.PropertyToID("_EdgeTexture"), sso.rt);

                
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public void Release(){
                CoreUtils.Destroy(screenSpaceOutlineMaterial);
                CoreUtils.Destroy(normalsMaterial);
                normals?.Release();
                sso?.Release();
            }
        }


        private class NonMaximumSuppressionPass : ScriptableRenderPass
        {
            private TemporalReprojection temporalReprojection;
            private readonly Material nmsMaterial;
            private readonly Material TAAMaterial;
             private readonly Material compositeMaterial;
            private RenderTargetIdentifier cameraColorTarget;
            private readonly List<ShaderTagId> shaderTagIdList;
            private RenderTargetIdentifier backBuffer1;
            private int backBuffer1_ID = Shader.PropertyToID("_CurrTex");


            private OutlineShaderMaterialSettings outlineShaderMaterialSettings;
            private FilteringSettings filteringSettings;

      


            RTHandle[] rthandles = new RTHandle[2];
            RenderTargetIdentifier[] rthandles_id = new RenderTargetIdentifier[2];
            RTHandle screen;
            RTHandle hbuffer;


            public NonMaximumSuppressionPass(RenderPassEvent renderPassEvent, OutlineShaderMaterialSettings outlineShaderMaterialSettings, LayerMask layerMask, TemporalReprojection temporalReprojection)
            {
                this.renderPassEvent = renderPassEvent;
                this.temporalReprojection = temporalReprojection;


                this.nmsMaterial = new Material(Shader.Find("Shader Graphs/NMSPass"));
                this.compositeMaterial = new Material(Shader.Find("Shader Graphs/CompositeOutline"));
                this.TAAMaterial = new Material(Shader.Find("Example/TemporalReprojection"));

                shaderTagIdList = new List<ShaderTagId> {   new ShaderTagId("UniversalForward"), 
                                                            new ShaderTagId("UniversalForwardOnly"), 
                                                            new ShaderTagId("LightweightForward"), 
                                                            new ShaderTagId("SRPDefaultUnlit"), };


                this.outlineShaderMaterialSettings = outlineShaderMaterialSettings;
                //filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);

                filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                
                
                cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

                // Create temporary buffer
                var desc = renderingData.cameraData.cameraTargetDescriptor;

                if(screen==null){
                    EffectBase.EnsureRenderTarget(ref screen, desc.width, desc.height, RenderTextureFormat.ARGB32, FilterMode.Bilinear, 0);
                    rthandles[0] = screen;
                    rthandles_id[0] = screen.nameID;
                }
                if(hbuffer==null){
                    EffectBase.EnsureRenderTarget(ref hbuffer, desc.width, desc.height, RenderTextureFormat.ARGB32, FilterMode.Bilinear, 0);
                    rthandles[1] = hbuffer;
                    rthandles_id[1] = hbuffer.nameID;
                }

                cmd.GetTemporaryRT(backBuffer1_ID, desc.width, desc.height, 0, FilterMode.Bilinear, desc.colorFormat, RenderTextureReadWrite.Default);
                backBuffer1 = new RenderTargetIdentifier(backBuffer1_ID);
                
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if(!nmsMaterial || !TAAMaterial || !compositeMaterial)
                {
                    Debug.LogError("No NMS material");
                    return;
                }

                

                var desc = renderingData.cameraData.cameraTargetDescriptor;
                
                EffectBase.EnsureRenderTarget(ref rthandles[0], desc.width, desc.height, RenderTextureFormat.ARGB32, FilterMode.Bilinear, 0);
                EffectBase.EnsureRenderTarget(ref rthandles[1], desc.width, desc.height, RenderTextureFormat.ARGB32, FilterMode.Bilinear, 0);

                
                compositeMaterial.SetColor("_OutlineColor", outlineShaderMaterialSettings.OutlineColor);

        
                

                UnityEngine.Vector4 jitterUV = temporalReprojection._frustumJitter.activeSample;
                jitterUV.x /= renderingData.cameraData.camera.pixelWidth;
                jitterUV.y /= renderingData.cameraData.camera.pixelHeight;
                jitterUV.z /= renderingData.cameraData.camera.pixelWidth;
                jitterUV.w /= renderingData.cameraData.camera.pixelHeight;

                
                TAAMaterial.SetVector("_JitterUV", jitterUV);
                //TAAMaterial.SetTexture("_CurrTex", screen);
                TAAMaterial.SetTexture("_PrevTex", hbuffer);
                TAAMaterial.SetFloat("_FeedbackMin", temporalReprojection.feedbackMin);                                                                           
                TAAMaterial.SetFloat("_FeedbackMax", temporalReprojection.feedbackMax);

        

                TemporalReprojection.SetShaderKeyword(TAAMaterial, TemporalReprojection.USE_YCOCG_KEYWORD, temporalReprojection.useYCoCg);
                TemporalReprojection.SetShaderKeyword(TAAMaterial, TemporalReprojection.USE_CLIPPING_KEYWORD, temporalReprojection.useClipping);
                TemporalReprojection.SetShaderKeyword(TAAMaterial, TemporalReprojection.USE_DILATION_KEYWORD, temporalReprojection.useDilation);
                TemporalReprojection.SetShaderKeyword(TAAMaterial, TemporalReprojection.UNJITTER_NEIGHBORHOOD_KEYWORD, temporalReprojection.UNJITTER_NEIGHBORHOOD);
                TemporalReprojection.SetShaderKeyword(TAAMaterial, TemporalReprojection.UNJITTER_REPROJECTION_KEYWORD, temporalReprojection.UNJITTER_REPROJECTION);
                TemporalReprojection.SetShaderKeyword(TAAMaterial, TemporalReprojection.UNJITTER_COLORSAMPLES_KEYWORD, temporalReprojection.UNJITTER_COLORSAMPLES);
                TemporalReprojection.SetShaderKeyword(TAAMaterial, TemporalReprojection.USE_OPTIMIZATIONS_KEYWORD, temporalReprojection.useOptimizations);
                EffectBase.EnsureKeyword(TAAMaterial, "USE_MINMAX_3X3", temporalReprojection.samplingMethod == TemporalReprojection.NeighborhoodSamplingMethod.MinMax_3x3);
                EffectBase.EnsureKeyword(TAAMaterial, "USE_MINMAX_3X3_ROUNDED", temporalReprojection.samplingMethod == TemporalReprojection.NeighborhoodSamplingMethod.MinMax_3x3_Rounded);
                
                //TemporalReprojection.SetNeighborhoodSamplingMethodKeyword(TAAMaterial, temporalReprojection.samplingMethod);

                nmsMaterial.SetFloat("_NMSAmount", outlineShaderMaterialSettings.NMS_OutlineScale);
                EffectBase.EnsureKeyword(nmsMaterial, "USE_NMS", outlineShaderMaterialSettings.useNMS);




                RTHandle cameraColorTarget_ = renderingData.cameraData.renderer.cameraColorTargetHandle;

                CommandBuffer cmd = CommandBufferPool.Get();
                using(new UnityEngine.Rendering.ProfilingScope(cmd, new ProfilingSampler("NMS_TAA")))
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    UnityEngine.Matrix4x4 projectionMatrix = renderingData.cameraData.camera.GetProjectionMatrix(temporalReprojection._frustumJitter.activeSample.x, temporalReprojection._frustumJitter.activeSample.y);
                    //cmd.SetProjectionMatrix(projectionMatrix);
                    cmd.SetProjectionMatrix(renderingData.cameraData.camera.projectionMatrix);

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    /*
                    if(temporalReprojection.useTAA)
                    {
                        UnityEngine.Matrix4x4 projectionMatrix = renderingData.cameraData.camera.GetProjectionMatrix(temporalReprojection._frustumJitter.activeSample.x, temporalReprojection._frustumJitter.activeSample.y);
                        cmd.SetProjectionMatrix(projectionMatrix);

                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();
                    }
                    */

                    
                    DrawingSettings drawingSettings = CreateDrawingSettings( shaderTagIdList, 
                                                                              ref renderingData,
                                                                              renderingData.cameraData.defaultOpaqueSortFlags);
                    context.DrawRenderers(  renderingData.cullResults,
                                        ref drawingSettings, 
                                        ref filteringSettings);
                    
        
                    if(temporalReprojection.useTAA)
                    {
  
                        cmd.Blit(null, backBuffer1, nmsMaterial);

                        
                            cmd.SetRenderTarget(rthandles_id, (RenderTargetIdentifier)BuiltinRenderTextureType.None);
                

                            cmd.SetViewProjectionMatrices(UnityEngine.Matrix4x4.identity, UnityEngine.Matrix4x4.identity);
                            cmd.DrawMesh(RenderingUtils.fullscreenMesh, UnityEngine.Matrix4x4.identity, TAAMaterial);
                            cmd.SetViewProjectionMatrices(renderingData.cameraData.camera.worldToCameraMatrix, renderingData.cameraData.camera.projectionMatrix);


                            compositeMaterial.SetTexture("_Screen", screen);
                            
                            //cmd.Blit(cameraColorTarget, cameraColorTarget, compositeMaterial);
                            Blit(cmd, cameraColorTarget, backBuffer1);
                            Blit(cmd, backBuffer1, cameraColorTarget, compositeMaterial);

                        
                    }
                    else
                    {
                        cmd.Blit(null, screen, nmsMaterial);

                        compositeMaterial.SetTexture("_Screen", screen);

                        Blit(cmd, cameraColorTarget, backBuffer1);
                        Blit(cmd, backBuffer1, cameraColorTarget, compositeMaterial);
                    }
                }

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(backBuffer1_ID);
            }
        }

        [SerializeField] private RenderPassEvent renderPassEvent;
        [SerializeField] private RenderPassEvent depthRenderPassEvent;
        [SerializeField] private RenderPassEvent outlineRenderPassEvent;
        [SerializeField] private TemporalReprojection temporalReprojection = new TemporalReprojection();
        [SerializeField] private ViewSpaceNormalsTextureSettings viewSpaceNormalsTextureSettings;
        [SerializeField] private VelocityBufferSettings velocityBufferSettings;
        [SerializeField] private CustomDepthTextureSettings customDepthTextureSettings;
        [SerializeField] private LayerMask outlinesLayerMask;
        [SerializeField] private LayerMask depthLayerMask;

        [SerializeField] private OutlineShaderMaterialSettings outlineShaderMaterialSettings;


        private ViewSpaceNormalsTexturePass viewSpaceNormalsTexturePass;
        private VelocityBufferTexturePass velocityBufferTexturePass;
        private CustomDepthTexturePass customDepthTexturePass;
        private ScreenSpaceOutlinesPass_New screenSpaceOutlinesPass;
        private NonMaximumSuppressionPass nonMaximumSuppressionPass;
        


        private bool requiresColor;
        private bool injectedBeforeTransparents;

        public override void Create()
        {
            viewSpaceNormalsTexturePass = new ViewSpaceNormalsTexturePass(renderPassEvent, viewSpaceNormalsTextureSettings, outlinesLayerMask, temporalReprojection);
            velocityBufferTexturePass = new VelocityBufferTexturePass(renderPassEvent, velocityBufferSettings, outlinesLayerMask, temporalReprojection);
            customDepthTexturePass = new CustomDepthTexturePass(depthRenderPassEvent, customDepthTextureSettings, depthLayerMask, temporalReprojection);
            screenSpaceOutlinesPass = new ScreenSpaceOutlinesPass_New(outlineRenderPassEvent, outlineShaderMaterialSettings, viewSpaceNormalsTextureSettings, outlinesLayerMask, temporalReprojection);
            nonMaximumSuppressionPass = new NonMaximumSuppressionPass(outlineRenderPassEvent, outlineShaderMaterialSettings,  outlinesLayerMask, temporalReprojection);    
     
            // This copy of requirements is used as a parameter to configure input in order to avoid copy color pass
            ScriptableRenderPassInput modifiedRequirements = velocityBufferSettings.requirements;

            requiresColor = (velocityBufferSettings.requirements & ScriptableRenderPassInput.Color) != 0;
            injectedBeforeTransparents = velocityBufferSettings.injectionPoint <= InjectionPoint.BeforeRenderingTransparents;

            if (requiresColor && !injectedBeforeTransparents)
            {
                // Removing Color flag in order to avoid unnecessary CopyColor pass
                // Does not apply to before rendering transparents, due to how depth and color are being handled until
                // that injection point.
                modifiedRequirements ^= ScriptableRenderPassInput.Color;
            }

            velocityBufferTexturePass.ConfigureInput(modifiedRequirements);

            temporalReprojection._frustumJitter.OnAwake(Camera.main);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Ensure the render feature only runs with the main camera
            if (renderingData.cameraData.camera.tag != "MainCamera")
            {
                return;
            }

            // Update the helper once per frame
            //temporalReprojection.OnUpdate();

            if(temporalReprojection.useTAA)
            {
                temporalReprojection._frustumJitter._OnPreCull(renderingData.cameraData.camera);
            }
            
            renderer.EnqueuePass(customDepthTexturePass);
            //renderer.EnqueuePass(velocityBufferTexturePass);
            //renderer.EnqueuePass(viewSpaceNormalsTexturePass);
            renderer.EnqueuePass(screenSpaceOutlinesPass);
            renderer.EnqueuePass(nonMaximumSuppressionPass);
        }



        
    }
}