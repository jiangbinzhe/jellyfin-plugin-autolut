"""Replay mobile playback contracts against a DISPOSABLE loopback Jellyfin 12.1.
Creates only synthetic media. Restores plugin configuration. Never prints credentials or stream URLs.
The supplied test data directory must not be a production deployment.
"""
import argparse
import copy
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.parse
import urllib.request
import uuid

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--base-url", default="http://127.0.0.1:18096")
    p.add_argument("--credentials", type=Path, required=True)
    p.add_argument("--media-directory", type=Path, required=True)
    p.add_argument("--ffmpeg", type=Path, required=True)
    p.add_argument("--report", type=Path, required=True)
    args = p.parse_args()
    parts = urllib.parse.urlsplit(args.base_url)
    if parts.scheme != "http" or parts.hostname not in ("127.0.0.1", "::1") or parts.username or parts.password:
        p.error("Only a disposable loopback HTTP test server is allowed")
    base=args.base_url.rstrip("/")
    pid="70a81c22-b61d-4bdd-bd86-2048e0b29a5a"
    authorization='MediaBrowser Client="AutoLutMobileContractTest", Device="Synthetic", DeviceId="mobile-contract-test", Version="0.1.2"'
    def call(path,data=None,method=None):
        request=urllib.request.Request(base+path,None if data is None else json.dumps(data).encode(),
            {"Content-Type":"application/json","Authorization":authorization},method=method)
        with urllib.request.urlopen(request,timeout=60) as response:
            content=response.read()
            return json.loads(content) if content else None
    assert call("/System/Info/Public")["Version"]=="12.1.0"
    cred=json.loads(args.credentials.read_text())
    auth=call("/Users/AuthenticateByName",{"Username":cred["username"],"Pw":cred["password"]},"POST")
    authorization+=', Token="'+auth["AccessToken"]+'"'
    assert any(x["Id"].replace("-","")==pid.replace("-","") and x["Version"]=="0.1.2.0" and x["Status"]=="Active"
        for x in call("/Plugins")), "Load the candidate plugin before running this contract test"
    uid=auth["User"]["Id"]
    media=args.media_directory.resolve()
    media.mkdir(parents=True,exist_ok=True)
    # Fixed reserved names inside the explicitly supplied disposable media directory.
    video=media/"AutoLut Mobile Contract.mp4"
    video.with_suffix(".en.srt").write_text("1\n00:00:00,000 --> 00:00:07,000\nAuto LUT subtitle contract\n")
    subprocess.run([str(args.ffmpeg),"-nostdin","-v","error","-f","lavfi","-i","color=c=0xc18d73:s=640x360:r=24",
        "-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-f","lavfi","-i","sine=frequency=880:sample_rate=48000",
        "-t","8","-map","0:v","-map","1:a","-map","2:a","-vf","setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709",
        "-c:v","libx264","-pix_fmt","yuv420p","-bsf:v","h264_metadata=colour_primaries=1:transfer_characteristics=1:matrix_coefficients=1",
        "-c:a","aac","-metadata:s:a:0","language=eng","-metadata:s:a:1","language=jpn","-y",str(video)],check=True)
    folders=call("/Library/VirtualFolders")
    if not any(f["Name"]=="AutoLut Mobile Contract" for f in folders):
        call("/Library/VirtualFolders?name=AutoLut%20Mobile%20Contract&collectionType=movies&refreshLibrary=true",
            {"LibraryOptions":{"EnableRealtimeMonitor":False,"EnableInternetProviders":False,"PathInfos":[{"Path":str(media)}]}},"POST")
    else: call("/Library/Refresh",{},"POST")
    item=None
    for _ in range(40):
        items=call("/Items?Recursive=true&IncludeItemTypes=Movie&Fields=Path,MediaSources,MediaStreams")["Items"]
        item=next((i for i in items if i.get("Path")==str(video) and any(s.get("Type")=="Subtitle" for s in i.get("MediaStreams",[]))),None)
        if item: break
        time.sleep(0.5)
    assert item,"Synthetic media/subtitle scan incomplete"
    iid=item["Id"]
    sid=item["MediaSources"][0]["Id"]
    sub=next(s["Index"] for s in item["MediaStreams"] if s["Type"]=="Subtitle")
    audio=[s["Index"] for s in item["MediaStreams"] if s["Type"]=="Audio"]
    original=call("/Plugins/"+pid+"/Configuration")
    config=dict(original)
    cache=Path("/tmp/autolut-mobile-"+uuid.uuid4().hex)
    report={"server":"12.1.0","plugin":"0.1.2.0","method":"HTTP replay of representative source-derived requests; not an app/device test",
        "profiles":[],"checks":[],"qsv_pixels":"not-tested","device_playback":"not-tested"}
    checks=report["checks"]
    active=[]
    def save(): call("/Plugins/"+pid+"/Configuration",config,"POST")
    def request(body):
        return call("/Items/"+iid+"/PlaybackInfo?enableDirectPlay=true&allowVideoStreamCopy=true",body,"POST")
    def cubes(): return len(list(cache.rglob("lut.cube"))) if cache.exists() else 0
    def stop(response):
        session=response["PlaySessionId"]
        call("/Sessions/Playing",{"ItemId":iid,"MediaSourceId":sid,"PlaySessionId":session,"PlayMethod":"Transcode","CanSeek":True},"POST")
        call("/Sessions/Playing/Stopped",{"ItemId":iid,"MediaSourceId":sid,"PlaySessionId":session,"PositionTicks":20_000_000},"POST")
        if session in active: active.remove(session)
    try:
        config.update({"Enabled":True,"UserIds":uid,"ItemIds":iid,"DeviceIds":"mobile-contract-test","Mode":"Automatic",
            "CacheDirectory":str(cache),"FfmpegPath":str(args.ffmpeg),"MaxSessions":1})
        save()
        for fixture in sorted((Path(__file__).parent/"fixtures").glob("*.json")):
            profile=json.loads(fixture.read_text())
            # Android generally omits UserId; Swiftfin sends it and normally omits StartTimeTicks.
            body={"DeviceProfile":profile,"AutoOpenLiveStream":True,"MediaSourceId":sid,
                "MaxStreamingBitrate":3_000_000,"SubtitleStreamIndex":-1}
            if fixture.stem.startswith("android"): body["StartTimeTicks"]=20_000_000
            else: body["UserId"]=uid
            for case in ("plain","text-subtitle","audio-switch"):
                posted=copy.deepcopy(body)
                if case=="text-subtitle": posted["SubtitleStreamIndex"]=sub
                if case=="audio-switch":
                    posted["AudioStreamIndex"]=audio[-1]
                    if fixture.stem.startswith("android"): posted["StartTimeTicks"]=30_000_000
                n=cubes()
                response=request(posted)
                active.append(response["PlaySessionId"])
                source=response["MediaSources"][0]
                assert not source["SupportsDirectPlay"] and not source["SupportsDirectStream"], fixture.stem+" "+case+" not forced"
                assert source["SupportsTranscoding"] and source["TranscodingSubProtocol"].lower()=="hls"
                query={k.lower():v for k,v in urllib.parse.parse_qs(urllib.parse.urlsplit(source["TranscodingUrl"]).query).items()}
                assert query["videocodec"]==["h264"],"Expected H264-only negotiation"
                assert query["allowvideostreamcopy"]==["false"]
                assert query["playsessionid"]==[response["PlaySessionId"]]
                assert query["deviceid"]==["mobile-contract-test"]
                assert cubes()==n+1, fixture.stem+" "+case+" LUT not retained/bound"
                if "StartTimeTicks" in posted and "starttimeticks" in query: assert query["starttimeticks"]==[str(posted["StartTimeTicks"])] # HLS can seek on the full timeline without this URL parameter.
                if "AudioStreamIndex" in posted: assert source["DefaultAudioStreamIndex"]==audio[-1]
                if case=="text-subtitle":
                    selected=next(s for s in source["MediaStreams"] if s["Index"]==source["DefaultSubtitleStreamIndex"])
                    assert selected["DeliveryMethod"] in ("External","Hls"),"Text subtitle would be burned in"
                # Encoder stop is not playback stop: Android uses it during seek/rebuild.
                call("/Videos/ActiveEncodings?deviceId=mobile-contract-test&playSessionId="+response["PlaySessionId"],method="DELETE")
                assert cubes()==n+1
                # No slot is available until the authenticated playback-stop report.
                held=request(body)
                assert cubes()==n+1,"Encoder stop incorrectly freed a live session"
                stop(response)
                checks.append(fixture.stem+":"+case+":h264-hls-session-subtitle-audio-stop-pass")
            report["profiles"].append(fixture.stem)
        # No HLS profile: downloads and forced-direct profiles must bypass without consuming a slot.
        body={"MediaSourceId":sid,"SubtitleStreamIndex":-1,"DeviceProfile":copy.deepcopy(profile)}
        body["DeviceProfile"]["TranscodingProfiles"]=[]
        n=cubes(); normal=request(body)
        assert normal["MediaSources"][0]["SupportsDirectPlay"] and cubes()==n
        checks.append("forced-direct-profile-bypasses")
        body["DeviceProfile"]["TranscodingProfiles"]=[{"Type":"Video","Container":"mp4","VideoCodec":"h264","AudioCodec":"aac","Protocol":"http","Context":"Static"}]
        request(body); assert cubes()==n
        checks.append("download-profile-bypasses")
        body["DeviceProfile"]=copy.deepcopy(profile)
        body["SubtitleStreamIndex"]=sub
        body["AlwaysBurnInSubtitleWhenTranscoding"]=True
        request(body); assert cubes()==n
        checks.append("burn-in-bypasses")
        config["DeviceIds"]="excluded-device"; save()
        body["SubtitleStreamIndex"]=-1; body.pop("AlwaysBurnInSubtitleWhenTranscoding")
        normal=request(body)
        assert normal["MediaSources"][0]["SupportsDirectPlay"] and cubes()==n
        checks.append("other-device-direct-play")
        report["retained_luts"]=n
        report["success"]=True
        args.report.parent.mkdir(parents=True,exist_ok=True)
        args.report.write_text(json.dumps(report,indent=2))
        print(json.dumps(report,indent=2))
    finally:
        for session in active:
            try: call("/Sessions/Playing/Stopped",{"ItemId":iid,"PlaySessionId":session},"POST")
            except Exception: pass
        call("/Plugins/"+pid+"/Configuration",original,"POST")
        # Keep test-generated LUTs for diagnosis; paths/tokens are deliberately absent from report.

if __name__=="__main__": main()
