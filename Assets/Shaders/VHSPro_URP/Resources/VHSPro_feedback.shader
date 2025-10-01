Shader "Hidden/VHSPro_feedback"{
   HLSLINCLUDE
    
      //Blit.hlsl provides the vertex shader (Vert), the input structure (Attributes), and the output structure (Varyings)
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

      //Note: "_BlitTexture" is the default input texture name when we use Blitter.BlitTexture  
      TEXTURE2D_X(_TexLast); 
      TEXTURE2D_X(_TexFeedbackLast); 


      float feedbackAmount;
      float feedbackFade;
      float feedbackThresh;
      half3 feedbackColor;


      half3 bms(half3 a, half3 b){ return 1.-(1.-a)*(1.-b); }
      half grey(half3 a){ return (a.x+a.y+a.z)/3.; }
      half len(half3 a, half3 b){ return (abs(a.x-b.x)+abs(a.y-b.y)+abs(a.z-b.z))/3.; }


      float4 VHSPro_Feedback(Varyings input) : SV_Target {

         UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

         float2 p = input.texcoord; // og normalized tex coordnates 0..1  
         float one_x = 1./_ScreenParams.x;

         //new feedback value
         half3 fc = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, p).xyz; 
         half3 fl = SAMPLE_TEXTURE2D(_TexLast,     sampler_LinearClamp, p).xyz; 

         float diff = grey(saturate(fc-fl)); //dfference between frames
         if( diff<feedbackThresh ) diff = 0.; //TODO step() cut off 

         //feedback new
         //v4: lets remove og color
         half3 fbCurrent = diff * feedbackAmount; 
         // half3 fbn = fc * diff * feedbackAmount; //feedback new


         //old feedback buffer
         //let's blur it a bit
         half G0 = .452, G1 = .274; // Gaussian distribution coefficients 
         half3 fbLast =  
            SAMPLE_TEXTURE2D(_TexFeedbackLast, sampler_LinearClamp, p).xyz * G0 +
            SAMPLE_TEXTURE2D(_TexFeedbackLast, sampler_LinearClamp, (p+ float2(one_x,0)) ).xyz * G1 +
            SAMPLE_TEXTURE2D(_TexFeedbackLast, sampler_LinearClamp, (p- float2(one_x,0)) ).xyz * G1;

         fbLast *= feedbackFade;
         // if( (fbb.x+fbb.y+fbb.z)/3.<.01 ) fbb = half3(0,0,0);

         half3 fb = bms(fbCurrent, fbLast); 

         return float4(fb * feedbackColor, 1.); 

         //TODO we can add color on Bleed Pass, and keep everything here in 1 channel  

      }


   ENDHLSL
    
   SubShader {

      Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
      LOD 100
      ZWrite Off Cull Off
        
      // pass 0
      Pass {

         Name "VHSPro Feedback Pass"

         HLSLPROGRAM
            
         #pragma vertex Vert
         #pragma fragment VHSPro_Feedback
            
         ENDHLSL
      }
        
   }         

}