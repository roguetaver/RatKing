using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule; 
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;


using VladStorm;

public class VHSProRenderPass : ScriptableRenderPass { 
    
   //RenderGraph passes it as a parameter to the rendering function
   class PassData {
      // Texture handles do not need to be disposed/freed (they are auto-invalidated at the end of graph execution).
      internal TextureHandle src;
      internal Material mat;
   }

   class PassDataNoMat {
      internal TextureHandle src;
   }


   //Refs
   protected VHSProVolumeComponent vc;     //volume component from Volume Post Processing Stack

   //Passes names
   const string passName_Tape =      "VHSPro Tape Pass";
   const string passName_Signal =    "VHSPro Signal Pass";
   const string passName_Feedback1 = "VHSPro Feedback Pass";
   const string passName_Feedback2 = "VHSPro Copy texSignalOut to texLast";
   const string passName_Feedback3 = "VHSPro Copy texFeedback to texFeedbackLast";
   const string passName_Bleed =     "VHSPro Bleed Pass";
   const string passName_Bypass =    "VHSPro Bypass";



   //Materials
   Material matTape;      //noises pass (tape noise, etc)
   Material matSignal;    //signal pass (distortion, etc)
   Material matFeedback;  //feedback pass
   Material matBleed;     //bleeding + mix with feedback

   RTHandle rthSignalOut;
   RTHandle rthTapeOut;
   RTHandle rthFeedback;
   RTHandle rthFeedbackLast;
   RTHandle rthLast;
   RTHandle rthBypass;

   const string texName_Signal =       "_TexSignal";
   const string texName_Tape =         "_TexTape";
   const string texName_Feedback =     "_TexFeedback";
   const string texName_FeedbackLast = "_TexFeedbackLast";
   const string texName_Last =         "_TexLast";
   const string texName_Bypass =       "_TexBypass";


   RenderTextureDescriptor desc;
   static Vector4 scaleBias = new Vector4(1f, 1f, 0f, 0f);
   float _time = 0f;
   Vector4 _ResOg;   //original screen resolution (.xy resolution .zw one pixel)
   Vector4 _Res;     //resolution after pixelation
   Vector4 _ResN;    //resolution of noise



   //Runs only once, on Pass Initialization during ShaderFeature Initialization
   public VHSProRenderPass() { 

      //init palettes and resolution presets
      VHSHelper.Init();

      desc = new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.Default, 0);

      if(matTape==null)       LoadMat(ref matTape,       "Materials/VHSPro_tape");
      if(matSignal==null)     LoadMat(ref matSignal,     "Materials/VHSPro_signal");
      if(matFeedback==null)   LoadMat(ref matFeedback,   "Materials/VHSPro_feedback");
      if(matBleed==null)      LoadMat(ref matBleed,      "Materials/VHSPro_bleed");

   }
 

   //Runs every frame
   public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {

      // ---
      // Initialization 
      // ---

      UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
      UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

      vc = VolumeManager.instance.stack.GetComponent<VHSProVolumeComponent>();
      if (vc==null){
         Debug.LogError($"Unable to find VHSPro Volume Component");
         return;
      }

      if (!vc.active) { // || !IsActive()
         return;
      }

      // ensures that the render pass doesn't blit from the back buffer.
      if (resourceData.isActiveTargetBackBuffer)
         return;

      // Skipping post processing rendering inside the scene view
      if (cameraData.isSceneViewCamera) 
         return;

      if (matTape==null || matSignal==null || matBleed==null || matFeedback==null){
         Debug.Log("Materials aren't initialized");
         return;
      }


      //Resolution Presents
      ResPreset resPreset = VHSHelper.GetResPresets()[ vc.screenResPresetId.value ];
      if(resPreset.isCustom!=true){
         vc.screenWidth.value  = resPreset.screenWidth;
         vc.screenHeight.value = resPreset.screenHeight;
      }
      if(resPreset.isFirst==true || vc.pixelOn.value==false){
         vc.screenWidth.value  = desc.width;
         vc.screenHeight.value = desc.height;
      }


      if(vc.independentTimeOn.value)   _time = Time.unscaledTime; 
      else                             _time = Time.time; 

      //original screen resolution (.xy resolution .zw one pixel)
      _ResOg = new Vector4(desc.width, desc.height, 0f, 0f);
      _ResOg[2] = 1f/_ResOg.x; 
      _ResOg[3] = 1f/_ResOg.y;  

      //resolution after pixelation
      _Res = new Vector4(vc.screenWidth.value, vc.screenHeight.value, 0f,0f);
      _Res[2] = 1f/_Res.x;                                    
      _Res[3] = 1f/_Res.y;                                    

      //resolution of noise 
      _ResN = new Vector4(_Res.x, _Res.y, _Res.z, _Res.w);
      if(!vc.noiseResGlobal.value){
         _ResN = new Vector4(vc.noiseResWidth.value, vc.noiseResHeight.value, 0f, 0f);
         _ResN[2] = 1f/_ResN.x;                                    
         _ResN[3] = 1f/_ResN.y;                                                
      }

      var spIds = VHSHelper.spIds;


      //Init textures

      //Set the texture size to be the same as the camera target size
      desc.width = cameraData.cameraTargetDescriptor.width;
      desc.height = cameraData.cameraTargetDescriptor.height;
      desc.depthBufferBits = 0;

      //Note: ReAllocateHandleIfNeeded(..) re-allocates fixed-size RTHandle if it is not allocated or doesn't match the descriptor
      RenderingUtils.ReAllocateHandleIfNeeded(ref rthSignalOut,      desc, FilterMode.Point, TextureWrapMode.Clamp, name: texName_Signal);
      RenderingUtils.ReAllocateHandleIfNeeded(ref rthTapeOut,        desc, FilterMode.Point, TextureWrapMode.Clamp, name: texName_Tape);      
      RenderingUtils.ReAllocateHandleIfNeeded(ref rthFeedback,       desc, FilterMode.Point, TextureWrapMode.Clamp, name: texName_Feedback);

      //these are passed to the next frame    
      RenderingUtils.ReAllocateHandleIfNeeded(ref rthLast,           desc, FilterMode.Point, TextureWrapMode.Clamp, name: texName_Last );
      RenderingUtils.ReAllocateHandleIfNeeded(ref rthFeedbackLast,   desc, FilterMode.Point, TextureWrapMode.Clamp, name: texName_FeedbackLast );         
      
      RenderingUtils.ReAllocateHandleIfNeeded(ref rthBypass,   desc, FilterMode.Point, TextureWrapMode.Clamp, name: texName_Bypass );         



      // ---
      // Execution Part
      // ---

      bool isNoisePass =      (vc.tapeNoiseOn.value || vc.filmgrainOn.value || vc.lineNoiseOn.value);
      // bool isBleedPass =      vc.bleedOn.value;
      bool isFeedbackPass =   vc.feedbackOn.value;
      bool isPaletteOn =      vc.paletteOn.value;
      bool isBypassOn =       vc.bypassOn.value;

      //1. Noise Pass
      if(isNoisePass){

         using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName_Tape, out var passData)) {

            //Note: Texture handles do not need to be disposed/freed (they are auto-invalidated at the end of graph execution)
            TextureHandle texSrc = resourceData.activeColorTexture; //lets use cam texture for input, but we actually dont need it 
            TextureHandle texDst = renderGraph.ImportTexture(rthTapeOut);
            if (!texSrc.IsValid() || !texDst.IsValid())
               return;

            passData.src = texSrc;
            passData.mat = matTape;

            //inputs and outputs
            builder.UseTexture(passData.src, AccessFlags.Read);         //input
            builder.SetRenderAttachment(texDst, 0, AccessFlags.Write);  //output texture
            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => {

               matTape.SetFloat(spIds["_time"],  _time);  
               matTape.SetVector(spIds["_ResN"], _ResN); 

               FeatureToggle(matTape, vc.filmgrainOn.value, "VHS_FILMGRAIN_ON");
               matTape.SetFloat(spIds["filmGrainAmount"], vc.filmGrainAmount.value);
               
               FeatureToggle(matTape, vc.lineNoiseOn.value, "VHS_LINENOISE_ON");
               matTape.SetFloat(spIds["lineNoiseAmount"], vc.lineNoiseAmount.value);
               matTape.SetFloat(spIds["lineNoiseSpeed"],  vc.lineNoiseSpeed.value);

               FeatureToggle(matTape, vc.tapeNoiseOn.value, "VHS_TAPENOISE_ON");
               matTape.SetFloat(spIds["tapeNoiseAmt"],      vc.tapeNoiseAmt.value);
               matTape.SetFloat(spIds["tapeNoiseSpeed"],    vc.tapeNoiseSpeed.value);
               // matTape.SetFloat(spIds["tapeNoiseAlpha"], vc.tapeNoiseAlpha.value);

               //Note: input texture is called "_BlitTexture"               
               Blitter.BlitTexture(ctx.cmd, data.src, scaleBias, data.mat, 0);

            });
         }
      }



      //Pass 2. Signal Pass

      //bypass texture
      if(isBypassOn){      

         // using (var builder = renderGraph.AddRasterRenderPass<PassDataNoMat>(passName_Bypass, out var passData)) {

         //    TextureHandle texSrc =  renderGraph.ImportTexture(vc.bypassTex.value);                   
         //    TextureHandle texDest = renderGraph.ImportTexture(rthBypass); 
         //    if (!texSrc.IsValid() || !texDest.IsValid())
         //       return;

         //    passData.src = texSrc;

         //    //inputs and outputs
         //    builder.UseTexture(passData.src, AccessFlags.Read);
         //    builder.SetRenderAttachment(texDest, 0, AccessFlags.Write);
         //    builder.SetRenderFunc((PassDataNoMat data, RasterGraphContext ctx) => {
         //       Blitter.BlitTexture(ctx.cmd, passData.src, scaleBias, 0, false); //copy textures without material    
         //    });
         // }
         //TODO remove
         Graphics.Blit(vc.bypassTex.value, rthBypass.rt);

     }


      using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName_Signal, out var passData)) {

         TextureHandle texSrc = resourceData.activeColorTexture;         //input: camera texture
         if(isBypassOn){
            texSrc = renderGraph.ImportTexture(rthBypass);
         }
         TextureHandle texDst = renderGraph.ImportTexture(rthSignalOut); //output: to RTHandle SignalOut 
         if (!texSrc.IsValid() || !texDst.IsValid() )
            return;

         passData.src = texSrc;
         passData.mat = matSignal;

         //inputs and outputs
         builder.UseTexture(passData.src, AccessFlags.Read);          //input. name is _BlitTexture ?
         builder.SetRenderAttachment(texDst, 0, AccessFlags.Write);   //output: Use the texture as an RenderTarget attachment
         builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => {

            matSignal.SetFloat(spIds["_time"],      _time);  
            matSignal.SetVector(spIds["_ResOg"],    _ResOg);

            //Pixelation
            matSignal.SetVector(spIds["_Res"],      _Res);
            matSignal.SetVector(spIds["_ResN"],     _ResN);

            //Color Decimation
            FeatureToggle(matSignal, vc.colorOn.value, "VHS_COLOR");       
             
            matSignal.SetInt(spIds["_colorMode"],        vc.colorMode.value);
            matSignal.SetInt(spIds["_colorSyncedOn"],    vc.colorSyncedOn.value?1:0);

            matSignal.SetInt(spIds["bitsR"],             vc.bitsR.value);
            matSignal.SetInt(spIds["bitsG"],             vc.bitsG.value);
            matSignal.SetInt(spIds["bitsB"],             vc.bitsB.value);
            matSignal.SetInt(spIds["bitsSynced"],        vc.bitsSynced.value);

            matSignal.SetInt(spIds["bitsGray"],          vc.bitsGray.value);
            matSignal.SetColor(spIds["grayscaleColor"],  vc.grayscaleColor.value);        

            FeatureToggle(matSignal, vc.ditherOn.value, "VHS_DITHER");        
            matSignal.SetInt(spIds["_ditherMode"],       vc.ditherMode.value);
            matSignal.SetFloat(spIds["ditherAmount"],    vc.ditherAmount.value);


            //Signal Tweak
            FeatureToggle(matSignal, vc.signalTweakOn.value, "VHS_SIGNAL_TWEAK_ON");

            matSignal.SetFloat(spIds["signalAdjustY"], vc.signalAdjustY.value);
            matSignal.SetFloat(spIds["signalAdjustI"], vc.signalAdjustI.value);
            matSignal.SetFloat(spIds["signalAdjustQ"], vc.signalAdjustQ.value);

            matSignal.SetFloat(spIds["signalShiftY"], vc.signalShiftY.value);
            matSignal.SetFloat(spIds["signalShiftI"], vc.signalShiftI.value);
            matSignal.SetFloat(spIds["signalShiftQ"], vc.signalShiftQ.value);


            //Palette
            FeatureToggle(matSignal, vc.paletteOn.value, "VHS_PALETTE");
            if(isPaletteOn){

               PalettePreset pal = VHSHelper.GetPalettes()[ vc.paletteId.value ];
               Texture2D texPaletteSorted = pal.texSortedPre;                   
               matSignal.SetTexture(spIds["_TexPalette"], texPaletteSorted);

               matSignal.SetInt(spIds["_ResPalette"],       pal.texSortedWidth);
               matSignal.SetInt(spIds["paletteDelta"],      vc.paletteDelta.value);

            }

            //VHS 1st Pass (Distortions, Decimations) 
            
            //Noise
            FeatureToggle(matSignal, vc.filmgrainOn.value, "VHS_FILMGRAIN_ON");
            FeatureToggle(matSignal, vc.tapeNoiseOn.value, "VHS_TAPENOISE_ON");
            FeatureToggle(matSignal, vc.lineNoiseOn.value, "VHS_LINENOISE_ON");

            if(isNoisePass) {
               matSignal.SetTexture(spIds["_TexTape"], rthTapeOut);
               matSignal.SetFloat(spIds["tapeNoiseAlpha"], vc.tapeNoiseAlpha.value); 
            }


            //Jitter & Twitch
            FeatureToggle(matSignal, vc.jitterHOn.value, "VHS_JITTER_H_ON");
            matSignal.SetFloat(spIds["jitterHAmount"], vc.jitterHAmount.value);

            FeatureToggle(matSignal, vc.jitterVOn.value, "VHS_JITTER_V_ON");
            matSignal.SetFloat(spIds["jitterVAmount"], vc.jitterVAmount.value);
            matSignal.SetFloat(spIds["jitterVSpeed"], vc.jitterVSpeed.value);

            FeatureToggle(matSignal, vc.linesFloatOn.value, "VHS_LINESFLOAT_ON");     
            matSignal.SetFloat(spIds["linesFloatSpeed"], vc.linesFloatSpeed.value);

            FeatureToggle(matSignal, vc.twitchHOn.value, "VHS_TWITCH_H_ON");
            matSignal.SetFloat(spIds["twitchHFreq"], vc.twitchHFreq.value);
            // cmd.SetGlobalFloat(Shader.PropertyToID(spIds["twitchHFreq"]), vc.twitchHFreq.value);

            FeatureToggle(matSignal, vc.twitchVOn.value, "VHS_TWITCH_V_ON");
            matSignal.SetFloat(spIds["twitchVFreq"], vc.twitchVFreq.value);

            FeatureToggle(matSignal, vc.scanLinesOn.value, "VHS_SCANLINES_ON");
            matSignal.SetFloat(spIds["scanLineWidth"], vc.scanLineWidth.value);

            FeatureToggle(matSignal, vc.signalNoiseOn.value, "VHS_YIQNOISE_ON");
            matSignal.SetFloat(spIds["signalNoisePower"], vc.signalNoisePower.value);
            matSignal.SetFloat(spIds["signalNoiseAmount"], vc.signalNoiseAmount.value);

            FeatureToggle(matSignal, vc.stretchOn.value, "VHS_STRETCH_ON");

            Blitter.BlitTexture(ctx.cmd, passData.src, scaleBias, data.mat, 0);      

         });
      }



      //Pass 3. Feedback Pass
      if(isFeedbackPass){

         //Feedback 1
         using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName_Feedback1, out var passData)) {

            TextureHandle texSrc =    renderGraph.ImportTexture(rthSignalOut);                   
            TextureHandle texDest =   renderGraph.ImportTexture(rthFeedback);
            if (!texSrc.IsValid() || !texDest.IsValid())
               return;

            passData.src = texSrc;
            passData.mat = matFeedback;

            //inputs and outputs
            builder.UseTexture(passData.src, AccessFlags.Read);         
            builder.SetRenderAttachment(texDest, 0, AccessFlags.Write); 
            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => {
      
               matFeedback.SetTexture(spIds["_TexLast"],          rthLast);
               matFeedback.SetTexture(spIds["_TexFeedbackLast"],  rthFeedbackLast);

               matFeedback.SetFloat(spIds["feedbackThresh"],   vc.feedbackThresh.value);
               matFeedback.SetFloat(spIds["feedbackAmount"],   vc.feedbackAmount.value);
               matFeedback.SetFloat(spIds["feedbackFade"],     vc.feedbackFade.value);
               
               //TODO move to bleed pass?
               matFeedback.SetColor(spIds["feedbackColor"],    vc.feedbackColor.value);

               Blitter.BlitTexture(ctx.cmd, passData.src, scaleBias, data.mat, 0);

            });
         }

         //Feedback 2. Copy rthSignalOut to rthLast
         using (var builder = renderGraph.AddRasterRenderPass<PassDataNoMat>(passName_Feedback2, out var passData)) {

            TextureHandle texSrc =    renderGraph.ImportTexture(rthSignalOut);                   
            TextureHandle texDest =   renderGraph.ImportTexture(rthLast); 
            if (!texSrc.IsValid() || !texDest.IsValid())
               return;

            passData.src = texSrc;
            // passData.mat = matFeedback;

            //inputs and outputs
            builder.UseTexture(passData.src, AccessFlags.Read);         //input
            builder.SetRenderAttachment(texDest, 0, AccessFlags.Write);  //output
            builder.SetRenderFunc((PassDataNoMat data, RasterGraphContext ctx) => {
               Blitter.BlitTexture(ctx.cmd, passData.src, scaleBias, 0, false); //copy textures without material    
            });
         }

         //Feedback 3. Copy texFeedback to texFeedbackLast
         using (var builder = renderGraph.AddRasterRenderPass<PassDataNoMat>(passName_Feedback3, out var passData)) {

            TextureHandle texSrc =  renderGraph.ImportTexture(rthFeedback);                   
            TextureHandle texDest = renderGraph.ImportTexture(rthFeedbackLast); 
            if (!texSrc.IsValid() || !texDest.IsValid())
               return;

            passData.src = texSrc;
            // passData.mat = matFeedback;

            //inputs and outputs
            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.SetRenderAttachment(texDest, 0, AccessFlags.Write);
            builder.SetRenderFunc((PassDataNoMat data, RasterGraphContext ctx) => {
               Blitter.BlitTexture(ctx.cmd, passData.src, scaleBias, 0, false); //copy textures without material    
            });
         }

         //Note: somehow these didnt work :c
         // renderGraph.AddCopyPass(texFeedback, texFeedbackLast, passName: "VHSPro copy texFeedback to texFeedbackLast"); 
         // renderGraph.AddCopyPass(texSignalOut, texLast, passName: "VHSPro copy texSignalOut to texLast"); 

      }



      //Pass 4. Bleed Pass
      //Note: we can run this pass with or without bleed part
      //we need to do it this way in order to have an option to add feedback without a bleed
         using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName_Bleed, out var passData)) {

            TextureHandle texSrc = renderGraph.ImportTexture(rthSignalOut);                   
            TextureHandle texDst = resourceData.activeColorTexture; 
            if (!texSrc.IsValid() || !texDst.IsValid())
               return;

            passData.src = texSrc;
            passData.mat = matBleed;

            //inputs and outputs
            builder.UseTexture(passData.src, AccessFlags.Read);
            builder.SetRenderAttachment(texDst, 0, AccessFlags.Write);
            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => {

               matBleed.SetFloat(spIds["_time"],   _time);  
               matBleed.SetVector(spIds["_ResOg"], _ResOg);  
               matBleed.SetVector(spIds["_Res"],   _Res);    

               //feedback
               matBleed.SetInt(spIds["feedbackOn"],            vc.feedbackOn.value?1:0);
               matBleed.SetInt(spIds["feedbackDebugOn"],       vc.feedbackDebugOn.value?1:0);
               if(isFeedbackPass){
                  matBleed.SetTexture("_TexFeedback", rthFeedback);
               }

               //CRT       

               FeatureToggle(matBleed, vc.bleedOn.value, "VHS_BLEED_ON");

               matBleed.DisableKeyword("VHS_OLD_THREE_PHASE");
               matBleed.DisableKeyword("VHS_THREE_PHASE");
               matBleed.DisableKeyword("VHS_TWO_PHASE");           
                    if(vc.crtMode.value==0){ matBleed.EnableKeyword("VHS_OLD_THREE_PHASE"); }
               else if(vc.crtMode.value==1){ matBleed.EnableKeyword("VHS_THREE_PHASE"); }
               else if(vc.crtMode.value==2){ matBleed.EnableKeyword("VHS_TWO_PHASE"); }

               matBleed.SetFloat(spIds["bleedAmount"], vc.bleedAmount.value);

               Blitter.BlitTexture(ctx.cmd, passData.src, scaleBias, data.mat, 0);      

            });

         }

   }

   public void Dispose() {

      //we shouldn't destroy materials 'cause they are resources      
      // CoreUtils.Destroy(matTape);
      // CoreUtils.Destroy(matSignal);
      // CoreUtils.Destroy(matFeedback);
      // CoreUtils.Destroy(matBleed);

      rthSignalOut?.Release();
      rthTapeOut?.Release();
      rthFeedback?.Release();
      rthFeedbackLast?.Release();
      rthLast?.Release();
      rthBypass?.Release();

   }


   //Helper Functions
   void FeatureToggle(Material mat, bool propVal, string featureName){  //turn on/off shader features
      if(propVal)     mat.EnableKeyword(featureName);
      else            mat.DisableKeyword(featureName);
   }

   void LoadMat(ref Material m, string filePath){      
      m = Resources.Load<Material>(filePath);
      if(m==null) 
         Debug.LogError($"Unable to find material '{filePath}.material'. VHSPro is unable to load.");
   }

}

