Shader "Hidden/VHSPro_signal"{

   HLSLINCLUDE
    
      //Blit.hlsl provides the vertex shader (Vert), the input structure (Attributes), and the output structure (Varyings)
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
      //Note: when we use Blitter.BlitTexture, the default input texture name is "_BlitTexture"


      TEXTURE2D(_TexTape);  
      TEXTURE2D(_TexPalette); 

      // List of properties to control your post process effect
      float    _time;
      float4   _ResOg;  // before pixelation (.xy resolution, .zw one pixel )
      float4   _Res;    // after pixelation  (.xy resolution, .zw one pixel )
      float4   _ResN;   // noise resolution  (.xy resolution, .zw one pixel )


      //Color Reduction Part (former Graphics Adapter Pro)
      #pragma shader_feature VHS_COLOR
      #pragma shader_feature VHS_PALETTE   
      #pragma shader_feature VHS_DITHER   
      #pragma shader_feature VHS_SIGNAL_TWEAK_ON   

      #include "vhs_gap.hlsl"


      //Signal Distortion Part
      #pragma shader_feature VHS_FILMGRAIN_ON
      #pragma shader_feature VHS_LINENOISE_ON
      #pragma shader_feature VHS_TAPENOISE_ON
      #pragma shader_feature VHS_YIQNOISE_ON
      #pragma shader_feature VHS_TWITCH_H_ON
      #pragma shader_feature VHS_TWITCH_V_ON  
      #pragma shader_feature VHS_JITTER_H_ON
      #pragma shader_feature VHS_JITTER_V_ON 
      #pragma shader_feature VHS_LINESFLOAT_ON
      #pragma shader_feature VHS_SCANLINES_ON
      #pragma shader_feature VHS_STRETCH_ON  

      #include "vhs_signal.hlsl" 


      float4 VHSPro_Signal(Varyings input) : SV_Target {

         UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
      
         float3 outColor = vhs(input);    
         return float4(outColor, 1);

      }

   ENDHLSL
    
   SubShader {

      Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
      LOD 100
      ZWrite Off Cull Off
        
      // pass 0
      Pass {

         Name "VHSPro Signal Pass"

         HLSLPROGRAM
            
         #pragma vertex Vert
         #pragma fragment VHSPro_Signal
            
         ENDHLSL
      }
        
   }         

}
