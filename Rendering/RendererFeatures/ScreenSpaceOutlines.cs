using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class ScreenSpaceOutlines : ScriptableRendererFeature
{
    [System.Serializable]
    private class ViewSpaceNormalsTextureSettings 
    {
        public Color backgroundColor = Color.white;
        public int depthBufferBits = 0;
        public RenderTextureFormat colorFormat = RenderTextureFormat.Default;
        public FilterMode filterMode = FilterMode.Bilinear; // Add this line

        [Range(0.0f, 1.0f)]
        public float resolutionScale = 1;
    }

    [System.Serializable]
    private class CustomDepthTextureSettings
    {
        public Color backgroundColor = Color.black;
        public int depthBufferBits = 0;
        public RenderTextureFormat colorFormat = RenderTextureFormat.Default;
        public FilterMode filterMode = FilterMode.Bilinear; // Add this line

        [Range(0.0f, 1.0f)]
        public float resolutionScale = 1;
    }

    [System.Serializable]
    private class OutlineShaderMaterialSettings
    {
        public float OutlineScale = 1.0f;
        public float RobertsCrossMultiplier = 1.0f;
        public float DepthThreshold = 1.0f;
        public float NormalThreshold = 1.0f;
        public float SteepAngleThreshold = 1.0f;
        public float SteepAngleMultiplier = 1.0f;
        public Color OutlineColor;

        public float Intensity = 1.0f;
        public float OutlineJitter = 1.0f;

        public float NoiseScale = 200.0f;

        public float MinDistance = 1.0f;
        public float MaxDistance = 1000.0f;
        public float DistancePow = 1.03f;
        public float GaussianBlurAmount = 0.1f;

        public int NMSTexelOffset = 1;
        [Range(0.0f, 1.0f)]
        public float NMSAmount = 1.0f;
    }

    private class CustomDepthTexturePass : ScriptableRenderPass
    {
        private CustomDepthTextureSettings depthTextureSettings;
        private readonly List<ShaderTagId> shaderTagIdList;
        private RenderTargetHandle depthTex;
        private readonly Material depthMaterial;
        private FilteringSettings filteringSettings;

        public CustomDepthTexturePass(RenderPassEvent renderPassEvent, CustomDepthTextureSettings settings, LayerMask layerMask)
        {
            shaderTagIdList = new List<ShaderTagId> { new ShaderTagId("DepthOnly")};

            this.renderPassEvent = renderPassEvent;
            depthTex.Init("_CustomDepthTexture");
            depthTextureSettings = settings;


            //Shader shader = Shader.Find("Unlit/CustomDepthPass");
            Shader shader = Shader.Find("Shader Graphs/DepthShader");
            this.depthMaterial=  new Material(shader);
            filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);
        }
        

         // called before render pass
         
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if(depthTextureSettings == null)
            {
                Debug.LogError("No depth texture settings");
                return;
            }
            // normalsTextureDescriptor setup
            RenderTextureDescriptor depthTextureDescriptor = cameraTextureDescriptor;
            depthTextureDescriptor.colorFormat = depthTextureSettings.colorFormat;
            depthTextureDescriptor.depthBufferBits = depthTextureSettings.depthBufferBits;

            depthTextureDescriptor.width = Mathf.RoundToInt(depthTextureDescriptor.width * depthTextureSettings.resolutionScale);
            depthTextureDescriptor.height = Mathf.RoundToInt(depthTextureDescriptor.height * depthTextureSettings.resolutionScale);

            // Create the custom depth texture
            cmd.GetTemporaryRT(depthTex.id, depthTextureDescriptor, depthTextureSettings.filterMode);


            ConfigureTarget(depthTex.Identifier());
            ConfigureClear(ClearFlag.All, depthTextureSettings.backgroundColor);
        }

         public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
        ConfigureTarget( depthTex.Identifier());
		}


        // executes the render pass
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            
            if(!depthMaterial)
            {
                Debug.LogError("No depth material");
                return;
            }
                
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
            DrawingSettings drawingSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, sortingCriteria);

            CommandBuffer cmd = CommandBufferPool.Get();
           
            using(new ProfilingScope(cmd, new ProfilingSampler("RenderToDepthTexture")))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                drawingSettings.overrideMaterial = depthMaterial;


                context.DrawRenderers(  renderingData.cullResults,
                                        ref drawingSettings, 
                                        ref filteringSettings);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

        }


        public override void OnCameraCleanup(CommandBuffer cmd){}
    }

    private class ViewSpaceNormalsTexturePass : ScriptableRenderPass
    {
        private ViewSpaceNormalsTextureSettings normalsTextureSettings;
        private readonly List<ShaderTagId> shaderTagIdList;
        private readonly RenderTargetHandle normals;

        private readonly Material normalsMaterial;

        private FilteringSettings filteringSettings;


        public ViewSpaceNormalsTexturePass(RenderPassEvent renderPassEvent, ViewSpaceNormalsTextureSettings settings, LayerMask outlinesLayerMask)
        {
            shaderTagIdList = new List<ShaderTagId> {   new ShaderTagId("UniversalForward"), 
                                                        new ShaderTagId("UniversalForwardOnly"), 
                                                        new ShaderTagId("LightweightForward"), 
                                                        new ShaderTagId("SRPDefaultUnlit"), };
            this.renderPassEvent = renderPassEvent;
            normals.Init("_SceneViewSpaceNormals");
            normalsTextureSettings = settings;


            Shader shader = Shader.Find("Shader Graphs/ViewSpaceNormalsShader");
            this.normalsMaterial =  new Material(shader);


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


            cmd.GetTemporaryRT(normals.id, normalTextureDescriptor, normalsTextureSettings.filterMode);

            ConfigureTarget(normals.Identifier());
            ConfigureClear(ClearFlag.All, normalsTextureSettings.backgroundColor);

     
        }

        // executes the render pass
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            
            if(!normalsMaterial)
            {
                Debug.LogError("No normal material");
                return;
            }
                

            CommandBuffer cmd = CommandBufferPool.Get();
           
            using(new ProfilingScope(cmd, new ProfilingSampler("SceneViewSpaceNormalsTextureCreation")))
            {
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
            CommandBufferPool.Release(cmd);
        }

        // called when the camera is finished rendering
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(normals.id);
        }

    }

    private class ScreenSpaceOutlinesPass : ScriptableRenderPass
    {
        private readonly Material screenSpaceOutlineMaterial;
        private RenderTargetIdentifier cameraColorTarget;
        private readonly List<ShaderTagId> shaderTagIdList;
        private RenderTargetIdentifier temporaryBuffer;
        private int temporaryBufferID = Shader.PropertyToID("_TemporaryBuffer");

        private OutlineShaderMaterialSettings outlineShaderMaterialSettings;

        private FilteringSettings filteringSettings;


        public ScreenSpaceOutlinesPass(RenderPassEvent renderPassEvent, OutlineShaderMaterialSettings outlineShaderMaterialSettings, LayerMask layerMask)
        {
            this.renderPassEvent = renderPassEvent;
            Shader shader = Shader.Find("Shader Graphs/OutlineShader");

            this.screenSpaceOutlineMaterial =  new Material(shader);

            shaderTagIdList = new List<ShaderTagId> {   new ShaderTagId("UniversalForward"), 
                                                new ShaderTagId("UniversalForwardOnly"), 
                                                new ShaderTagId("LightweightForward"), 
                                                new ShaderTagId("SRPDefaultUnlit"), };
            

            this.outlineShaderMaterialSettings = outlineShaderMaterialSettings;
            filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);
        }


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
           
            cameraColorTarget = renderingData.cameraData.renderer.cameraColorTarget;
            // Create temporary buffer
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            cmd.GetTemporaryRT(temporaryBufferID, desc.width, desc.height, 0, FilterMode.Point, desc.colorFormat, RenderTextureReadWrite.Linear);
            temporaryBuffer = new RenderTargetIdentifier(temporaryBufferID);

          
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(!screenSpaceOutlineMaterial)
                return;
        
            screenSpaceOutlineMaterial.SetFloat("_OutlineScale", outlineShaderMaterialSettings.OutlineScale);
            screenSpaceOutlineMaterial.SetFloat("_NoiseScale", outlineShaderMaterialSettings.NoiseScale);
            screenSpaceOutlineMaterial.SetFloat("_OutlineJitter", outlineShaderMaterialSettings.OutlineJitter);
            screenSpaceOutlineMaterial.SetFloat("_DepthThreshold", outlineShaderMaterialSettings.DepthThreshold);
            screenSpaceOutlineMaterial.SetFloat("_RobertsCrossMultiplier", outlineShaderMaterialSettings.RobertsCrossMultiplier);
            screenSpaceOutlineMaterial.SetFloat("_NormalThreshold", outlineShaderMaterialSettings.NormalThreshold);
            screenSpaceOutlineMaterial.SetFloat("_SteepAngleThreshold", outlineShaderMaterialSettings.SteepAngleThreshold);
            screenSpaceOutlineMaterial.SetFloat("_SteepAngleMultiplier", outlineShaderMaterialSettings.SteepAngleMultiplier);
            screenSpaceOutlineMaterial.SetColor("_OutlineColor", outlineShaderMaterialSettings.OutlineColor);
            screenSpaceOutlineMaterial.SetFloat("_Intensity", outlineShaderMaterialSettings.Intensity);
            screenSpaceOutlineMaterial.SetFloat("_MinDistance", outlineShaderMaterialSettings.MinDistance);
            screenSpaceOutlineMaterial.SetFloat("_MaxDistance", outlineShaderMaterialSettings.MaxDistance);
            screenSpaceOutlineMaterial.SetFloat("_DistancePow", outlineShaderMaterialSettings.DistancePow);
            screenSpaceOutlineMaterial.SetFloat("_GaussianBlurAmount", outlineShaderMaterialSettings.GaussianBlurAmount);
            screenSpaceOutlineMaterial.SetFloat("_NMS_TexelOffset", outlineShaderMaterialSettings.NMSTexelOffset);
             screenSpaceOutlineMaterial.SetFloat("_NMSAmount", outlineShaderMaterialSettings.NMSAmount);
           

            CommandBuffer cmd = CommandBufferPool.Get();
            using(new ProfilingScope(cmd, new ProfilingSampler("ScreenSpaceOutlines")))
            {

                DrawingSettings drawingSettings = CreateDrawingSettings( shaderTagIdList, 
                                                                ref renderingData,
                                                                renderingData.cameraData.defaultOpaqueSortFlags);
                context.DrawRenderers(  renderingData.cullResults,
                                    ref drawingSettings, 
                                    ref filteringSettings);

                Blit(cmd, cameraColorTarget, temporaryBuffer);
                Blit(cmd, temporaryBuffer, cameraColorTarget, screenSpaceOutlineMaterial);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(temporaryBufferID);
        }
    }



    [SerializeField] private RenderPassEvent renderPassEvent;
    [SerializeField] private ViewSpaceNormalsTextureSettings viewSpaceNormalsTextureSettings;
    [SerializeField] private CustomDepthTextureSettings customDepthTextureSettings;
    [SerializeField] private LayerMask outlinesLayerMask;
    [SerializeField] private LayerMask depthLayerMask;

    [SerializeField] private OutlineShaderMaterialSettings outlineShaderMaterialSettings;


    private ViewSpaceNormalsTexturePass viewSpaceNormalsTexturePass;
    private CustomDepthTexturePass customDepthTexturePass;
    private ScreenSpaceOutlinesPass screenSpaceOutlinesPass;


    public override void Create()
    {
        viewSpaceNormalsTexturePass = new ViewSpaceNormalsTexturePass(renderPassEvent, viewSpaceNormalsTextureSettings, outlinesLayerMask);
        customDepthTexturePass = new CustomDepthTexturePass(renderPassEvent, customDepthTextureSettings, depthLayerMask);
        screenSpaceOutlinesPass = new ScreenSpaceOutlinesPass(renderPassEvent, outlineShaderMaterialSettings,  outlinesLayerMask);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(viewSpaceNormalsTexturePass);
        renderer.EnqueuePass(customDepthTexturePass);
        renderer.EnqueuePass(screenSpaceOutlinesPass);
    }



    
}
