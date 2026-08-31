using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ChiakiNg.Tests;

/// <summary>
/// PP572: the workflow files read as documents, here, instead of on the runner.
///
/// Four readers in this tree open .github/workflows/build.yml and every one of them reads it as
/// text. BuildWorkflow finds runners, a dotnet version, repository paths and unexpanded $env:
/// references with string and regex work; GateAndCiAgree strips comment lines and says "Not a
/// parser" in as many words. That is the right call for the questions they ask - and not one of
/// them asks whether the file is YAML at all, so a syntax error in it is invisible until a push
/// spends a run. PP535 and PP567 exist because this repo has 39 red pushes and no green one.
///
/// It was edited three times in one session - PP567 added a filter, PP569 a step, PP570 a second
/// command inside one - and checked by hand afterwards, once, with python. That is care, not a
/// gate. This is the gate, and it covers every workflow in the directory rather than the one file
/// those edits happened to land in: roadkeep.yml is what runs `roadkeep lint` and site.yml is what
/// publishes the documentation area, and a syntax error in either is the same silence.
///
/// What is deliberately NOT here is whether a workflow does what it says. A document that parses
/// can still name a step that fails; that boundary is a runner's, and the four readers above are
/// where the claims about THIS checkout live.
/// </summary>
public class WorkflowParseTests(ITestOutputHelper output)
{
    /// <summary>Every .yml beside build.yml, or empty outside a checkout.</summary>
    private static IReadOnlyList<string> Workflows()
    {
        if (BuildWorkflow.Locate() is not { } build)
            return [];

        string[] found = Directory.GetFiles(Path.GetDirectoryName(build)!, "*.yml");
        Array.Sort(found, StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>
    /// The child a mapping holds under a plain key, or null.
    ///
    /// By the key scalar's own text rather than by indexing the mapping. YAML 1.1 resolves a plain
    /// `on` to a boolean tag, and GitHub's `on:` is the one key every workflow has - so a lookup
    /// built on node equality would miss exactly the key this file most needs to find.
    /// </summary>
    private static YamlNode? Child(YamlMappingNode map, string key)
    {
        foreach (KeyValuePair<YamlNode, YamlNode> entry in map.Children)
            if (entry.Key is YamlScalarNode { Value: { } text } && text == key)
                return entry.Value;

        return null;
    }

    /// <summary>The document a workflow file is, or a failure naming the line that stopped it.</summary>
    private static YamlNode RootOf(string path)
    {
        var stream = new YamlStream();

        try
        {
            using var reader = new StreamReader(path);
            stream.Load(reader);
        }
        catch (YamlException broken)
        {
            Assert.Fail(
                $"{Path.GetFileName(path)}:{broken.Start.Line}:{broken.Start.Column} is not YAML - "
                    + $"{broken.Message}. A push is the only other thing that reads this file.");
        }

        Assert.True(
            stream.Documents.Count == 1,
            $"{Path.GetFileName(path)} holds {stream.Documents.Count} documents; a workflow is one");

        return stream.Documents[0].RootNode;
    }

    /// <summary>
    /// THE GATE: every workflow in the directory parses.
    ///
    /// The count is asserted first, because a sweep that found no files would report every claim
    /// below as held - which is the shape PP271 and PP570 both went red over.
    /// </summary>
    [Fact]
    public void EveryWorkflowIsAYamlDocument()
    {
        IReadOnlyList<string> workflows = Workflows();
        if (workflows.Count == 0)
            return;

        Assert.True(
            workflows.Count >= 3,
            $"only {workflows.Count} workflow(s) found - build.yml, roadkeep.yml and site.yml are "
                + "all in this tree, so the sweep is not reading the directory");

        foreach (string path in workflows)
        {
            output.WriteLine(Path.GetFileName(path));
            Assert.IsType<YamlMappingNode>(RootOf(path));
        }
    }

    /// <summary>
    /// And each of them is a workflow, not merely well-formed YAML.
    ///
    /// `on` and `jobs` are what GitHub requires, and a job is either a run of steps on a runner or
    /// a call to another workflow. Both forms accepted, because both are legal and a check that
    /// knew only the first would refuse a valid file the day one is written; neither is optional,
    /// so a job with neither is the mistake this catches.
    /// </summary>
    [Fact]
    public void EveryWorkflowDeclaresJobsThatCanRun()
    {
        foreach (string path in Workflows())
        {
            string name = Path.GetFileName(path);
            var root = (YamlMappingNode)RootOf(path);

            Assert.True(Child(root, "on") is not null, $"{name} declares no trigger");

            YamlNode? jobs = Child(root, "jobs");
            var mapped = Assert.IsType<YamlMappingNode>(jobs);
            Assert.NotEmpty(mapped.Children);

            foreach (KeyValuePair<YamlNode, YamlNode> job in mapped.Children)
            {
                string id = (job.Key as YamlScalarNode)?.Value ?? "?";
                var body = Assert.IsType<YamlMappingNode>(job.Value);

                if (Child(body, "uses") is not null)
                    continue;

                Assert.True(
                    Child(body, "runs-on") is not null,
                    $"{name}: job '{id}' names no runner and calls no workflow");

                YamlNode? steps = Child(body, "steps");
                var listed = Assert.IsType<YamlSequenceNode>(steps);
                Assert.NotEmpty(listed.Children);

                foreach (YamlNode step in listed.Children)
                {
                    var declared = Assert.IsType<YamlMappingNode>(step);

                    Assert.True(
                        Child(declared, "run") is not null || Child(declared, "uses") is not null,
                        $"{name}: a step of job '{id}' neither runs a command nor uses an action");
                }
            }
        }
    }

    /// <summary>
    /// The reader says no about a broken file, which is the half that cannot be checked by reading
    /// green files.
    ///
    /// Two shapes: a value the scanner cannot finish, and a document whose root is a scalar. The
    /// second is what an accidental save of a comment header alone would leave, and it parses.
    /// </summary>
    [Fact]
    public void ABrokenWorkflowIsRejected()
    {
        var stream = new YamlStream();
        using var unterminated = new StringReader("jobs:\n  windows:\n    name: \"unclosed\n");
        Assert.ThrowsAny<YamlException>(() => stream.Load(unterminated));

        var scalar = new YamlStream();
        using var comment = new StringReader("# a header and nothing else\n");
        scalar.Load(comment);
        Assert.Empty(scalar.Documents);
    }
}
