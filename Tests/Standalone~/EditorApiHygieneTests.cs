using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Source-level guard against IMGUI / UI Toolkit API misuse in the Editor assembly.
    ///
    /// The window is pure UI Toolkit, so IMGUI-only event APIs must never appear in it.
    /// The canonical mistake is `evt.Use()` on a UI Toolkit event: EventBase has no Use()
    /// (that is IMGUI's Event.Use()), and Unity 2022.3 rejects it with CS1061. The offline
    /// Unity API stubs (Tools~/editor-api-check~) enforce this at compile time; these tests
    /// pin it at source level so the regression cannot come back even if the stubs drift.
    /// </summary>
    public static class EditorApiHygieneTests
    {
        /// <summary>IMGUI-only APIs that must not appear in the UI Toolkit Editor sources.</summary>
        private static readonly string[] BannedImguiCalls = { ".Use()" };

        private static List<string> EditorUiSources()
        {
            var files = new List<string>();
            Collect(new DirectoryInfo(Path.Combine(PackageLayout.Root, "Editor")), files);
            Assert.IsGreater(5, files.Count, "the Editor assembly has sources to scan, found " + files.Count + " file(s)");
            return files;
        }

        private static void Collect(DirectoryInfo dir, List<string> files)
        {
            if (!dir.Exists)
            {
                return;
            }

            foreach (var file in dir.GetFiles("*.cs", SearchOption.AllDirectories))
            {
                files.Add(file.FullName);
            }
        }

        /// <summary>
        /// Cuts line comments so explanatory text (e.g. "EventBase has no Use()") is not a
        /// violation. A comment cut only counts when `//` starts a line or is preceded by
        /// whitespace, so `https://` inside string literals stays intact.
        /// </summary>
        private static string StripLineComments(string line)
        {
            for (var i = 0; i + 1 < line.Length; i++)
            {
                if (line[i] == '/' && line[i + 1] == '/' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
                {
                    return line.Substring(0, i);
                }
            }

            return line;
        }

        [Test("no Editor source calls IMGUI's Event.Use() on a UI Toolkit event")]
        public static void NoImguiUseInUiToolkitSources()
        {
            var violations = new List<string>();
            foreach (var file in EditorUiSources())
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var lines = text.Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var code = StripLineComments(lines[i]);
                    foreach (var banned in BannedImguiCalls)
                    {
                        if (code.Contains(banned))
                        {
                            violations.Add(Path.GetFileName(file) + ":" + (i + 1) + " " + lines[i].Trim());
                        }
                    }
                }
            }

            Assert.AreEqual(0, violations.Count,
                "UI Toolkit events are consumed with StopPropagation()/PreventDefault(); " +
                "Event.Use() is IMGUI-only and fails to compile in Unity (CS1061). Found: " +
                string.Join(" | ", violations.ToArray()));
        }

        [Test("the drag-and-drop handlers keep validation, rejection and AcceptDrag behavior")]
        public static void DragDropContractIsPreserved()
        {
            var windowPath = Path.Combine(PackageLayout.Root, "Editor", "UI", "AvatarTunerWindow.cs");
            Assert.IsTrue(File.Exists(windowPath), "AvatarTunerWindow.cs exists to scan");
            var text = File.ReadAllText(windowPath, Encoding.UTF8);

            // Events are consumed the UI Toolkit way (all three DragPerformEvent paths).
            Assert.Contains("evt.StopPropagation();", text,
                "the drop handlers consume events with StopPropagation()");
            Assert.Contains("evt.PreventDefault();", text,
                "the drop handlers consume events with PreventDefault()");

            // Behavior preserved from the original drop zone.
            Assert.Contains("DragAndDrop.AcceptDrag();", text,
                "valid drops still accept the drag");
            Assert.Contains("DragAndDropVisualMode.Rejected", text,
                "invalid drags are still rejected");
            Assert.Contains("DragAndDropVisualMode.Copy", text,
                "valid drags still advertise a copy");
            Assert.Contains("\"ui.drop.invalidType\"", text,
                "non-GameObject drops still report the localized rejection message");
            Assert.Contains("DraggedGameObject()", text,
                "drop validation still resolves the dragged GameObject");
        }

        [Test("closing the optimization dialog (Cancel) touches no asset and no configuration")]
        public static void CancelDialogTouchesNothing()
        {
            var windowPath = Path.Combine(PackageLayout.Root, "Editor", "UI", "AvatarTunerWindow.cs");
            var text = File.ReadAllText(windowPath, Encoding.UTF8);

            var start = text.IndexOf("private void CloseOptimizeDialog()", StringComparison.Ordinal);
            var end = text.IndexOf("private void BuildDialog()", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0 && end > start, "the dialog close handler exists to scan");

            var body = StripLineComments(text.Substring(start, end - start));
            Assert.IsFalse(body.Contains("_controller."),
                "Cancel must not mutate controller configuration");
            Assert.IsFalse(body.Contains("AssetDatabase."),
                "Cancel must not touch assets");
            Assert.IsFalse(body.Contains("File.Write"),
                "Cancel must not write files");
            Assert.IsFalse(body.Contains("RunOptimization"),
                "Cancel must never trigger an optimization");
        }

        [Test("the credit footer wires both links with Unity's URL API and stays in the shell")]
        public static void FooterWiresBothLinks()
        {
            var windowPath = Path.Combine(PackageLayout.Root, "Editor", "UI", "AvatarTunerWindow.cs");
            var text = File.ReadAllText(windowPath, Encoding.UTF8);

            Assert.Contains("Application.OpenURL", text, "links open through Unity's URL API");
            Assert.Contains("https://github.com/awakenginexe/AvatarTuner", text,
                "the GitHub link points at the repository");
            Assert.Contains("https://awakenginexe.com/", text, "the developer link is configured");
            Assert.Contains("ui.footer.madeBy", text, "the made-by label is localized");
            Assert.Contains("Awake Ngine XE", text, "the developer name is never translated");

            var shellStart = text.IndexOf("private void BuildShell()", StringComparison.Ordinal);
            var shellEnd = text.IndexOf("private void TryLoadStylesheet()", StringComparison.Ordinal);
            Assert.IsTrue(shellStart >= 0 && shellEnd > shellStart, "the shell exists to scan");
            var shell = text.Substring(shellStart, shellEnd - shellStart);
            var footerAt = shell.IndexOf("BuildFooter()", StringComparison.Ordinal);
            var dialogAt = shell.IndexOf("_dialogArea", StringComparison.Ordinal);
            Assert.IsTrue(footerAt >= 0, "the footer lives in the window shell (not in any module)");
            Assert.IsTrue(footerAt < dialogAt,
                "the dialog overlay is added after the footer, so the dialog draws above it");
        }

        [Test("README language navigation and referenced images resolve")]
        public static void ReadmeLinksResolve()
        {
            var root = PackageLayout.Root;
            var readmes = new[] { "README.md", "README.th.md", "README.zh-Hans.md" };

            foreach (var readme in readmes)
            {
                var path = Path.Combine(root, readme);
                Assert.IsTrue(File.Exists(path), readme + " exists");
                var text = File.ReadAllText(path, Encoding.UTF8);

                // The current language is bold text; the other two are links.
                foreach (var link in readmes)
                {
                    if (link == readme)
                    {
                        continue;
                    }

                    Assert.Contains("(" + link + ")", text,
                        readme + " links to " + link + " in the language bar");
                }

                // Every referenced image must exist (the pending Size First capture is a
                // text note, never a broken image link).
                var marker = "](docs/images/";
                var index = 0;
                while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
                {
                    var start = index + marker.Length;
                    var end = text.IndexOf(')', start);
                    var relative = text.Substring(start, end - start);
                    Assert.IsTrue(File.Exists(Path.Combine(root, "docs", "images", relative)),
                        readme + " references " + relative + " which must exist on disk");
                    index = end;
                }
            }

            // The three captures referenced today.
            Assert.IsTrue(File.Exists(Path.Combine(root, "docs", "images", "original.png")),
                "the original capture exists");
            Assert.IsTrue(File.Exists(Path.Combine(root, "docs", "images", "quality-first.png")),
                "the quality-first capture exists");
            Assert.IsTrue(File.Exists(Path.Combine(root, "docs", "images", "balanced.png")),
                "the balanced capture exists");
        }
    }
}
