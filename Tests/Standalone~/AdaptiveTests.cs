using System;
using AvatarTuner.Core;
namespace AvatarTuner.StandaloneTests {
 public static class AdaptiveTests {
  [Test] public static void InheritanceAndRoundTrip() {
   var s = new OptimizationSettings(); s.Global.DesiredReduction=4; s.Global.ProtectQuality=true;
   s.Categories[SemanticCategory.Hair]=s.Global.Clone(); s.Categories[SemanticCategory.Hair].DesiredReduction=8;
   s.Textures["a"]=s.Categories[SemanticCategory.Hair].Clone(); s.Textures["a"].DesiredReduction=2; s.Textures["a"].ProtectQuality=false;
   var e=s.Resolve("a",SemanticCategory.Hair); Assert.AreEqual(SettingsSource.Custom,e.Source,"source"); Assert.AreEqual(2.0,e.Settings.DesiredReduction,"custom"); Assert.IsTrue(e.Settings.ProtectQuality,"inherited protection");
   var copy=OptimizationSettings.FromDictionary(s.ToDictionary()); Assert.AreEqual(s.ToHashToken(),copy.ToHashToken(),"roundtrip"); copy.Textures["a"].MaxResolution=512; Assert.AreNotEqual(s.ToHashToken(),copy.ToHashToken(),"hash");
  }
  [Test] public static void CandidatesRequireQuality() {
   var r=new TexturePlanRequest { SourceWidth=1024,SourceHeight=512,CurrentFormat=TunerTextureFormat.RGBA32,CurrentFormatKnown=true,Usage=TextureUsageKind.AlbedoOpaque };
   var s=new TextureSettings(); var p=PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
   var candidates=AdaptivePlanner.Candidates(r,p,null,s,SemanticCategory.Outfit); Assert.IsTrue(candidates.Count>2,"ladder");
   foreach(var c in candidates) Assert.AreEqual(c.Width/2,c.Height,"aspect");
   Assert.IsTrue(AdaptivePlanner.Select(r,p,null,s,SemanticCategory.Outfit,c=>new CandidateQuality()).IsNoOp,"no evidence");
   var chosen=AdaptivePlanner.Select(r,p,null,s,SemanticCategory.Outfit,c=>new CandidateQuality { Available=true,Psnr=60,Ssim=1 }); Assert.IsFalse(chosen.IsNoOp,"accepted");
   s.Excluded=true; Assert.IsTrue(AdaptivePlanner.Select(r,p,null,s,SemanticCategory.Outfit,c=>new CandidateQuality {Available=true,Psnr=60,Ssim=1}).IsNoOp,"excluded");
  }
  [Test] public static void ResamplingColorAndAlpha() {
   var pixels=new byte[]{0,0,0,255,255,255,255,255};
   var linear=TextureResampler.Resize(pixels,2,1,1,1,ResizeFilter.Area,false); var srgb=TextureResampler.Resize(pixels,2,1,1,1,ResizeFilter.Area,true);
   Assert.IsTrue(srgb[0]>linear[0],"linear light"); Assert.AreEqual(255,srgb[3],"alpha");
   var a=TextureResampler.Resize(new byte[]{255,0,0,0,0,0,255,255},2,1,1,1,ResizeFilter.Area,false,true); Assert.AreEqual(0,a[0],"premultiplied"); Assert.AreEqual(255,a[2],"blue");
  }
 }
}
