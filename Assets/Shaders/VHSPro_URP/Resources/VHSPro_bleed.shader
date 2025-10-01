Shader "Hidden/VHSPro_bleed"{

   HLSLINCLUDE
    
      //Blit.hlsl provides the vertex shader (Vert), the input structure (Attributes), and the output structure (Varyings)
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

      //Note: "_BlitTexture" is the default input texture name when we use Blitter.BlitTexture  
      TEXTURE2D_X(_TexFeedback); 


      #pragma shader_feature VHS_BLEED_ON
      #pragma shader_feature VHS_OLD_THREE_PHASE
      #pragma shader_feature VHS_THREE_PHASE
      #pragma shader_feature VHS_TWO_PHASE

      #pragma shader_feature VHS_SIGNAL_TWEAK_ON

      #include "vhs_bleed.hlsl" 

      float4 VHSPro_Bleed(Varyings input) : SV_Target {

         UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

         float3 outColor = bleed(input);
         return float4(outColor, 1);

      }


   ENDHLSL
    
   SubShader {

      Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
      LOD 100
      ZWrite Off Cull Off
        
      // pass 0
      Pass {

         Name "VHSPro Bleed Pass"

         HLSLPROGRAM
            
         #pragma vertex Vert
         #pragma fragment VHSPro_Bleed
            
         ENDHLSL
      }
        
   }         



}
