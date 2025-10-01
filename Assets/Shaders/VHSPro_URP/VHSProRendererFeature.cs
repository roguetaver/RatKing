using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


public class VHSProRendererFeature : ScriptableRendererFeature {

   VHSProRenderPass pass; //main render pass
   public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

   public override void Create() {

      this.name = "VHSPro";

      pass = new VHSProRenderPass();
      pass.renderPassEvent = renderPassEvent; 

   }


   //this method is called every frame, once for each camera
   public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
      
      if (renderingData.cameraData.cameraType != CameraType.Game) 
         return;

      renderer.EnqueuePass(pass);
      
   }

   protected override void Dispose(bool disposing) {

      pass?.Dispose();
      pass = null;

   }
    
}
