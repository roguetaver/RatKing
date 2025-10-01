Shader "Hidden/VHSPro_tape"{

    HLSLINCLUDE
    
      //Blit.hlsl provides the vertex shader (Vert), the input structure (Attributes), and the output structure (Varyings)
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
      //Note: when we use Blitter.BlitTexture, the default input texture name is "_BlitTexture"


      //Note: _ScreenParams (built-in) camera’s target texture
      float _time;
      float4 _ResN; //noise resolution

      //uniforms
      #pragma shader_feature VHS_TAPENOISE_ON
      float tapeNoiseAmt;     
      float tapeNoiseSpeed;    


      #pragma shader_feature VHS_FILMGRAIN_ON
      float filmGrainAmount;  
      float filmGrainPower;   
      
      #pragma shader_feature VHS_LINENOISE_ON
      float lineNoiseAmount;   
      float lineNoiseSpeed;    



      //Hash without sin coz trigonometry functions loose accuracy different GPUs
      //return [0..1]
      float hash12(float2 p){
         float3 p3  = frac( p.xyx * float3(443.8975,397.2973, 491.1871));
         p3 += dot(p3, p3.yzx + 19.19);
         return frac(p3.x * p3.z * p3.y);
      }

      //this hash works for big values
      //TODO use other sin indep hash for big values
      float hash( float n ){ return frac(sin(n)*43758.5453123); }

      //3D noise by iq
      float niq( in float3 x ){
         float3 p = floor(x);
         float3 f = frac(x);
         f = f*f*(3.-2.*f);
         float n = p.x + p.y*57. + 113.*p.z;
         return lerp(lerp( lerp( hash(n+  0.), hash(n+  1.),f.x),
                           lerp( hash(n+ 57.), hash(n+ 58.),f.x),f.y),
                     lerp( lerp( hash(n+113.), hash(n+114.),f.x),
                           lerp( hash(n+170.), hash(n+171.),f.x),f.y),f.z);
      }



      //Tape Noise

      //this part only responsible for tape noise lines
      //TODO separate it for later effects 
      float tapeNoiseLines(float2 p, float t){

         //atm line noise is depending on hash for int values
         //TODO rewrite for hash for 0..1 values 
         //then i can use normilized p for generating lines
         float y = p.y*_ScreenParams.y;
         float s = t * 2.;
         return    (niq( float3(y*.01 +s,        1., 1.) ) + 0.)
                  *(niq( float3(y*.011+1000.+s,  1., 1.) ) + 0.) 
                  *(niq( float3(y*.51+421.+s,    1., 1.) ) + 0.);
      }

      //@vladstorm tape noise, p here shud be normilized
      float tapeNoise(float nl, float2 p, float t){

         //TODO custom adjustable density (probability distribution)
         // but will be expensive (atm its ok)

         //atm its just contrast noise 
         
         //noise mask
         float nm =  hash12( frac(p+t*float2(.234, .637)) ) 
                     // *hash12( frac(p+t*float2(0.123,0.867)) ) 
                     // *hash12( frac(p+t*float2(0.441,0.23)) );
                     ;                 
         nm = nm*nm*nm*nm + .3; //cheap and ok
         //nm += 0.3 ; //just bit brighter or just more to threshold?

         nl*= nm; // put mask
         // nl += 0.3; //Value add .3//

         //threshold v4
         //TODO rewrite with step()
         if(nl<(1.-tapeNoiseAmt) ) nl = 0.; else nl =1.;  
         // if(nl<tapeNoiseAmt) nl = 0.; else nl =1.;  //threshold
         return nl;
      }



      #if VHS_LINENOISE_ON

         float rnd_rd(float2 co){
            float a = 12.9898, b = 78.233, c = 43758.5453;
            float dt = dot(co.xy ,float2(a,b));
            float sn = fmod(dt,3.14);
            return frac(sin(sn) * c);
         }

         float rndln(float2 p, float t){
            float sample = rnd_rd(float2(1.0,2.0*cos(t))*t*8.0 + p*1.0).x;
            sample *= sample;//*sample;
            return sample;
         }

         float lineNoise(float2 p, float t){
            
            float n = rndln(p* float2(0.5,1.0) + float2(1.0,3.0), t)*20.0;
            
            float freq = abs(sin(t));  //1.
            float c = n*smoothstep(fmod(p.y*4.0 + t/2.0+sin(t + sin(t*0.63)),freq), 0.0,0.95);

            return c;
         }

      #endif


      #if VHS_FILMGRAIN_ON

         float filmGrain(float2 uv, float t, float c ){ 
            
            // expensive but controllable w c
            // return n4rand(uv,t,c);
            
            //cheap noise
            float nr = hash12( uv + .07*frac( t ) );
            return nr*nr*nr;
         }  

         // //adjustable but expensive gausian noise
         // //t- time, c - amount 0..1
         // float n4rand( float2 n, float t, float c ) {
         //    t = frac( t );
         //    float nrnd0 = hash12( n + 0.07*t );            
         //     float p = 1. / (9.0*c);
         //    nrnd0 -= 0.5;
         //    nrnd0 *= 2.0;
         //    if(nrnd0<0.0) nrnd0 = pow(1.0+nrnd0, p)*0.5;
         //    else nrnd0 = 1.0-pow(nrnd0, p)*0.5;
         //    return nrnd0; 
         // }

      #endif




      float4 VHSPro_Tape(Varyings input) : SV_Target {

         UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

         float t = _time;             
         float2 p = input.texcoord; // og normalized tex uvs 0..1  
         
         //UV pixelation (URP)
         //Note: lets skip it here and pixilate it in the main pass - coz otherwise it causes visual bug
         // p = floor(p*_ResN.xy) / _ResN.xy;

         #if UNITY_UV_STARTS_AT_TOP 
            p.y = 1-p.y; 
         #endif

         float2 p_ = p*_ScreenParams.xy;

         float ns = 0.;    //signal noise
         float nt = 0.;    //tape noise
         float nl = 0.;    //lines for tape noise
         float ntail = 0.; //tails values for tape noise 

         #if VHS_TAPENOISE_ON

            //p is normilized (0..1)
            nl = tapeNoiseLines(p, t*tapeNoiseSpeed) ;   
            nt = tapeNoise(nl, p, t*tapeNoiseSpeed) ;    
            ntail = hash12(p+ float2(0.01,0.02) );

         #endif

         #if VHS_LINENOISE_ON
            ns += lineNoise(p_, t * lineNoiseSpeed) * lineNoiseAmount;
         #endif

         //y noise from yiq
         #if VHS_FILMGRAIN_ON                
            float bg = filmGrain((p_-0.5 * _ScreenParams.xy)*.5, t, filmGrainPower);
            ns += bg * filmGrainAmount;
         #endif

         return float4(nt,nl,ns,ntail); 

      }

   ENDHLSL
    
   SubShader {

      Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
      LOD 100
      ZWrite Off Cull Off
        
      // pass 0
      Pass {

         Name "VHSPro Tape Pass"

         HLSLPROGRAM
            
         #pragma vertex Vert
         #pragma fragment VHSPro_Tape
            
         ENDHLSL
      }
        
   }         


}

