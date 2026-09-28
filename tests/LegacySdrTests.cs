using System.Diagnostics;
using System.Text.Json;
using Jellyfin.Plugin.AutoLut;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;

public static class LegacySdrTests
{
    public static void PolicyChecks(Action<bool,string> check, string media)
    {
        var v = new MediaStream { Type=MediaStreamType.Video, Codec="h264", Width=1920, Height=1080, BitDepth=8,
            ColorPrimaries="smpte170m", ColorTransfer="smpte170m", ColorSpace="smpte170m", IsInterlaced=true };
        var source = new MediaSourceInfo { Path=media, Protocol=MediaProtocol.File, MediaStreams=[v] };
        var c = new Configuration { AssumeUnspecifiedBt709=true, Allow4kSdr=true };
        check(!c.EnableLegacySdr && !Policy.Eligible(source,c,out _), "legacy SDR requires its own opt-in");
        c.EnableLegacySdr=true;
        check(Policy.Eligible(source,c,out _) && !Policy.NeedsBt709Assumption(v,c), "known SMPTE170M interlaced source accepted without relabel assumption");
        check(v.ColorSpace=="smpte170m" && v.IsInterlaced, "normalization does not mutate shared source metadata");
        v.IsInterlaced=false; check(Policy.Eligible(source,c,out _), "progressive SMPTE170M also supported");
        v.ColorPrimaries=v.ColorTransfer=v.ColorSpace="bt709"; v.IsInterlaced=true;
        check(Policy.Eligible(source,c,out _), "interlaced BT709 uses deinterlace without color conversion");
        v.ColorPrimaries=v.ColorTransfer=v.ColorSpace=null;
        check(Policy.Eligible(source,c,out _), "interlaced untagged source requires both opt-ins");
        c.AssumeUnspecifiedBt709=false; check(!Policy.Eligible(source,c,out _), "legacy option alone cannot infer absent colors");
        v.ColorPrimaries=v.ColorTransfer=v.ColorSpace="smpte170m";
        v.ColorTransfer=null; check(!Policy.Eligible(source,c,out _), "partial SMPTE170M tags rejected");
        v.ColorTransfer="bt709"; check(!Policy.Eligible(source,c,out _), "mixed SMPTE170M tags rejected");
        v.ColorTransfer="smpte2084"; check(!Policy.Eligible(source,c,out _), "legacy option rejects PQ");
        v.ColorTransfer="arib-std-b67"; check(!Policy.Eligible(source,c,out _), "legacy option rejects HLG");
        v.ColorTransfer="smpte170m";
        v.BitDepth=10; check(!Policy.Eligible(source,c,out _), "legacy option rejects 10-bit source"); v.BitDepth=8;
        v.DvProfile=8; check(!Policy.Eligible(source,c,out _), "legacy option rejects Dolby Vision"); v.DvProfile=null;
        v.Hdr10PlusPresentFlag=true; check(!Policy.Eligible(source,c,out _), "legacy option rejects HDR10+"); v.Hdr10PlusPresentFlag=null;
        v.Width=3841; check(!Policy.Eligible(source,c,out _), "legacy option respects UHD width cap"); v.Width=1921;
        check(!Policy.Eligible(source,c,out _), "colorspace requires even source dimensions"); v.Width=1920;
        v.Rotation=90; check(!Policy.Eligible(source,c,out _), "legacy option rejects rotation"); v.Rotation=null;
        c.EnableLegacySdr=false; check(!Policy.Eligible(source,c,out _), "disabling legacy option restores strict rejection");
    }

    // Scalar reference independent of FFmpeg: limited-range BT.601 YCbCr,
    // SMPTE-C -> D65 XYZ -> BT.709 primaries, then limited-range BT.709 YCbCr.
    // The primary matrix is derived from chromaticities SMPTE-C:
    // R(.630,.340), G(.310,.595), B(.155,.070); BT.709:
    // R(.640,.330), G(.300,.600), B(.150,.060); white D65(.3127,.3290).
    // Use the piecewise 170M/709 transfer (the FFmpeg colorspace convention).
    // zscale's default display-referred BT.1886 curve is a different reference.
    private static double[] Smpte170mReference(byte yCode, byte uCode, byte vCode)
    {
        var y=(yCode-16)/219.0; var cb=(uCode-128)/224.0; var cr=(vCode-128)/224.0;
        var r=y+1.402*cr; var b=y+1.772*cb; var g=(y-.299*r-.114*b)/.587;
        static double Linear(double x) => Math.CopySign(Math.Abs(x)<.081 ? Math.Abs(x)/4.5 : Math.Pow((Math.Abs(x)+.099)/1.099,1/.45),x);
        static double Encoded(double x) => Math.CopySign(Math.Abs(x)<.018 ? Math.Abs(x)*4.5 : 1.099*Math.Pow(Math.Abs(x),.45)-.099,x);
        r=Linear(r); g=Linear(g); b=Linear(b);
        var rr=Math.Clamp(Encoded(.939542*r+.050181*g+.010277*b),0,1);
        var gg=Math.Clamp(Encoded(.017772*r+.965793*g+.016436*b),0,1);
        var bb=Math.Clamp(Encoded(-.001622*r-.004370*g+1.005992*b),0,1);
        var yy=.2126*rr+.7152*gg+.0722*bb;
        return [16+219*yy,128+224*(bb-yy)/1.8556,128+224*(rr-yy)/1.5748];
    }

    public static string CommandChecks(Action<bool,string> check, string command, string cube)
    {
        var interlaced = command.Replace("scale_vaapi=", CommandPatch.Bt709Parameters + ",deinterlace_vaapi=rate=frame,scale_vaapi=");
        check(CommandPatch.TryApply(interlaced,cube,out var patched,out var reason,false,true,true), "observed VAAPI deinterlace graph accepted");
        check(patched.Contains(CommandPatch.Smpte170mParameters + ",deinterlace_vaapi=rate=frame,scale_vaapi"), "source color retained ahead of deinterlace and scaling");
        check(reason.Contains("smpte170m-to-bt709") && reason.Contains("vaapi-deinterlace"), "applied log identifies conversion and hardware deinterlace");
        check(!patched[..patched.IndexOf("hwdownload")].Contains(CommandPatch.Bt709Parameters), "premature BT709 relabel removed");
        check(CommandPatch.TryApply(interlaced.Replace("rate=frame","rate=field"),cube,out var field,out _,false,true,true)
            && field.Contains("deinterlace_vaapi=rate=field"), "Jellyfin field-rate choice preserved");
        check(CommandPatch.TryApply(command,cube,out _,out _,false,true,false), "progressive color conversion requires no deinterlacer");
        check(CommandPatch.TryApply(interlaced,cube,out _,out _,false,false,true), "BT709 deinterlacing requires no color conversion");
        foreach(var bad in new[] { command, interlaced.Replace("deinterlace_vaapi","bwdif"),
            interlaced.Replace("rate=frame","rate=frame,deinterlace_vaapi=rate=frame"),
            interlaced.Replace("format=nv12:extra_hw_frames=24", "format=nv12:out_color_matrix=bt709:extra_hw_frames=24"),
            interlaced.Replace("nv12","p010le"),
            interlaced.Replace("deinterlace_vaapi=rate=frame,","").Replace(",hwmap=",",deinterlace_vaapi=rate=frame,hwmap=") })
            check(!CommandPatch.TryApply(bad,cube,out var unchanged,out _,false,true,true) && unchanged==bad, "missing/unsafe deinterlace or color-changing graph rejected unchanged");
        check(!CommandPatch.TryApply(interlaced,cube,out _,out _,true,true,true), "contradictory color plan rejected");
        check(!CommandPatch.TryApply(patched,cube,out _,out _,false,true,true), "legacy graph cannot be patched twice");
        return patched.Split("hwdownload,")[1].Split(",hwupload=")[0];
    }

    public static async Task PipelineChecks(Action<bool,string> check, string ffmpeg, string temp, string cpu, string cube)
    {
        async Task<byte[]> Run(string exe, params string[] args)
        {
            var info=new ProcessStartInfo(exe) { UseShellExecute=false, RedirectStandardError=true, RedirectStandardOutput=true };
            foreach(var arg in args) info.ArgumentList.Add(arg);
            using var process=Process.Start(info)!; using var output=new MemoryStream();
            var error=process.StandardError.ReadToEndAsync(); await process.StandardOutput.BaseStream.CopyToAsync(output);
            await process.WaitForExitAsync();
            if(process.ExitCode!=0) throw new Exception("Legacy SDR test failed: "+await error);
            await error; return output.ToArray();
        }
        async Task<byte[]> Pixels(string input, string graph, string format="nv12", string frames="1") => await Run(ffmpeg,
            "-nostdin","-v","error","-filter_threads","1","-f","lavfi","-i",input,"-frames:v",frames,
            "-vf",graph,"-pix_fmt",format,"-c:v","rawvideo","-threads:v","1","-f","rawvideo","pipe:1");
        var baseline=System.Text.RegularExpressions.Regex.Replace(cpu,@"lut3d=[^,]+,","");
        foreach(var color in new[] { "0xc18d73", "red", "green", "blue", "black", "white", "gray" })
        {
            var input="color=c="+color+":s=64x36:r=3,"+CommandPatch.Smpte170mParameters;
            var actual=await Pixels(input,cpu); var reference=await Pixels(input,baseline);
            check(actual.Length==64*36*3/2 && actual.Zip(reference,(a,b)=>Math.Abs(a-b)).Max()<=1, "identity LUT preserves full SMPTE170M conversion: "+color);
            var source=await Pixels(input,"format=nv12");
            var expected=Smpte170mReference(source[0],source[64*36],source[64*36+1]);
            var measured=new[] {(double)actual[0],actual[64*36],actual[64*36+1]};
            var error=measured.Zip(expected,(a,b)=>Math.Abs(a-b)).Max();
            check(error<=2, "conversion agrees with independent piecewise-transfer reference: "+color+" max="+error.ToString("0.###"));
        }
        var pattern="testsrc2=s=64x36:r=6,"+CommandPatch.Smpte170mParameters;
        var converted=await Pixels(pattern,cpu);
        var relabelled=await Pixels(pattern,CommandPatch.Bt709Parameters+",scale=in_color_matrix=bt709,format=gbrpf32le,scale=out_color_matrix=bt709,format=nv12");
        check(converted.Zip(relabelled,(a,b)=>Math.Abs(a-b)).Max()>5, "conversion changes pixels rather than only tags");
        var analysisGraph=LutService.AnalysisFilter(32,18,false,true,true);
        var interlacedPattern=pattern+",interlace=scan=tff";
        var analysis=await Pixels(interlacedPattern,analysisGraph,"rgba","3");
        check(analysis.Length==32*18*4*3 && analysis.Max()-analysis.Min()>100, "analysis deinterlaces moving synthetic video and converts color");
        var encoded=Path.Combine(temp,"legacy-progressive.mkv");
        await Run(ffmpeg,"-nostdin","-v","error","-filter_threads","1","-f","lavfi","-i",interlacedPattern,"-frames:v","3",
            "-vf","bwdif=mode=send_frame:parity=auto:deint=all,"+cpu,"-c:v","libx264","-threads:v","1","-y",encoded);
        using var probe=JsonDocument.Parse(await Run(Path.Combine(Path.GetDirectoryName(ffmpeg)!,"ffprobe"),"-v","error","-show_entries","stream=color_space,color_transfer,color_primaries,field_order","-of","json",encoded));
        var tags=probe.RootElement.GetProperty("streams")[0];
        check(new[] {"color_space","color_transfer","color_primaries"}.All(k=>tags.GetProperty(k).GetString()=="bt709")
            && tags.GetProperty("field_order").GetString()=="progressive", "software reference output is progressive BT709");
        var c=new Configuration { EnableLegacySdr=true, Mode="Fixed", FixedCubePath=cube, CacheDirectory=Path.Combine(temp,"legacy-cache") };
        var v=new MediaStream { BitDepth=8,Width=64,Height=36,IsInterlaced=true,ColorPrimaries="smpte170m",ColorTransfer="smpte170m",ColorSpace="smpte170m" };
        using var service=new LutService(NullLogger<LutService>.Instance);
        var plan=await service.Prepare(c,Guid.NewGuid(),"test",Guid.NewGuid(),encoded,v,0,CancellationToken.None);
        c.EnableLegacySdr=false;
        check(plan is {ConvertSmpte170m:true,Deinterlace:true,AssumeBt709:false}, "legacy plan remains frozen after settings change");
    }
}
