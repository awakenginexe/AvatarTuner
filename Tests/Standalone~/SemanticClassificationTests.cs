using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    public static class SemanticClassificationTests
    {
        [Test("renderer evidence outweighs filename")]
        public static void RendererWins()
        {
            var result = SemanticClassifier.Classify("Body", new[] { new SemanticEvidence { RendererPath = "Avatar/Hair_Front", PropertyName = "_MainTex" } });
            Assert.AreEqual(SemanticCategory.Hair, result.Category, "renderer");
            Assert.AreEqual(SemanticConfidence.High, result.Confidence, "confidence");
        }

        [Test("shared face and body texture is mixed")]
        public static void SharedIsMixed()
        {
            var result = SemanticClassifier.Classify("shared", new[] { new SemanticEvidence { RendererPath = "Avatar/Face" }, new SemanticEvidence { RendererPath = "Avatar/Body" } });
            Assert.AreEqual(SemanticCategory.Mixed, result.Category, "shared");
        }

        [Test("mask property takes precedence over renderer category")]
        public static void MaskProperty()
        {
            var result = SemanticClassifier.Classify("x", new[] { new SemanticEvidence { RendererPath = "Avatar/Face", PropertyName = "_EmissionMask" } });
            Assert.AreEqual(SemanticCategory.Masks, result.Category, "mask");
        }

        [Test("ambiguous substrings and generic main texture stay unknown")]
        public static void Unknown()
        {
            var result = SemanticClassifier.Classify("chair", new[] { new SemanticEvidence { RendererPath = "Avatar/Headphones", PropertyName = "_MainTex" } });
            Assert.AreEqual(SemanticCategory.Unknown, result.Category, "unknown");
        }

        [Test("nearest hierarchy name wins over its parent")]
        public static void NearestPath()
        {
            var result = SemanticClassifier.Classify("x", new[] { new SemanticEvidence { RendererPath = "Avatar/Body/Hair" } });
            Assert.AreEqual(SemanticCategory.Hair, result.Category, "nearest name");
        }

        [Test("animated material path contributes evidence")]
        public static void Animated()
        {
            var result = SemanticClassifier.Classify("x", new[] { new SemanticEvidence { RendererPath = "Avatar/Eyes", IsAnimated = true } });
            Assert.AreEqual(SemanticCategory.Eyes, result.Category, "animated");
            Assert.IsTrue(result.Reasons.Count > 0, "reason");
        }

        [Test("specific bone evidence helps generic meshes while generic skeleton roots stay unknown")]
        public static void RelevantBonesAreEvidence()
        {
            var result = SemanticClassifier.Classify("x", new[] { new SemanticEvidence { RendererPath = "Avatar/Mesh", BoneName = "Hair_Front" } });
            Assert.AreEqual(SemanticCategory.Hair, result.Category, "specific root bone");
            Assert.AreEqual(SemanticConfidence.Medium, result.Confidence, "bone hint is fallible");
            result = SemanticClassifier.Classify("x", new[] { new SemanticEvidence { RendererPath = "Avatar/Mesh", BoneName = "Hips" } });
            Assert.AreEqual(SemanticCategory.Unknown, result.Category, "generic skeleton root provides no category");
        }
    }
}
