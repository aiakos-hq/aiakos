using System.Text.Json;

using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class RigCanonicalizerTests
{
    [Fact]
    public void CANsharedWritesTheExactSharedTreeWithoutMutatingTheSnapshot()
    {
        var rig = MinimalRig();
        var snapshot = rig with { };

        var result = RigCanonicalizer.Create(rig);

        Assert.Equal(Golden("minimal.shared.json"), result.SharedJson);
        Assert.Equal(snapshot, rig);
        Assert.DoesNotContain("content", result.SharedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base64", result.SharedJson, StringComparison.OrdinalIgnoreCase);

        var full = OrderingRig() with
        {
            Culture = new EmbeddedFile("culture.md", "sha256:culture", [0, 255])
        };
        using var fullDocument = JsonDocument.Parse(RigCanonicalizer.Create(full).SharedJson);
        var root = fullDocument.RootElement;
        Assert.Equal(["agents", "culture", "description", "name", "repos", "seats"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["bytes", "path", "sha256"], root.GetProperty("culture").EnumerateObject()
            .Select(property => property.Name));
        var agent = root.GetProperty("agents")[0];
        Assert.Equal(["default_harness", "default_model", "description", "directory", "guidance",
            "harness_settings", "name", "skills"], agent.EnumerateObject().Select(property => property.Name));
        var guidance = agent.GetProperty("guidance")[0];
        Assert.Equal(["bytes", "path", "sha256"], guidance.EnumerateObject().Select(property => property.Name));
        var skill = agent.GetProperty("skills")[1];
        Assert.Equal(["description", "directory", "files", "name"],
            skill.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["bytes", "path", "sha256"], skill.GetProperty("files")[0].EnumerateObject()
            .Select(property => property.Name));
        var human = root.GetProperty("seats").EnumerateArray()
            .Single(seat => seat.GetProperty("kind").GetString() == "human");
        Assert.Equal(JsonValueKind.Null, human.GetProperty("agent").ValueKind);
        Assert.Equal(["agent", "description", "id", "kind"],
            human.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("Content", root.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CANorderCanonicalizesUnorderedArraysAndUsesCanonicalElementTies()
    {
        var rig = OrderingRig();
        var reversed = rig with
        {
            Repos = rig.Repos.Reverse().ToArray(),
            Agents = rig.Agents.Reverse().Select(agent => agent with
            {
                Skills = agent.Skills.Reverse().Select(skill => skill with
                {
                    Files = skill.Files.Reverse().ToArray()
                }).ToArray()
            }).ToArray(),
            Seats = rig.Seats.Reverse().Select(seat => seat with
            {
                Agent = seat.Agent is null ? null : seat.Agent with
                {
                    Repos = seat.Agent.Repos.Reverse().ToArray(),
                    Requires = seat.Agent.Requires with
                    {
                        Secrets = seat.Agent.Requires.Secrets.Reverse().ToArray()
                    }
                }
            }).ToArray(),
            Binding = rig.Binding with
            {
                Placement = rig.Binding.Placement.Reverse().ToArray(),
                Repos = rig.Binding.Repos.Reverse().ToArray(),
                Secrets = rig.Binding.Secrets.Reverse().ToArray()
            }
        };

        Assert.Equal(RigCanonicalizer.Create(rig).SharedJson, RigCanonicalizer.Create(reversed).SharedJson);
        Assert.Equal(RigCanonicalizer.Create(rig).BindingJson, RigCanonicalizer.Create(reversed).BindingJson);

        var reorderedGuidance = rig with
        {
            Agents = [rig.Agents[0] with { Guidance = rig.Agents[0].Guidance.Reverse().ToArray() }, rig.Agents[1]]
        };
        Assert.NotEqual(RigCanonicalizer.Create(rig).SharedJson, RigCanonicalizer.Create(reorderedGuidance).SharedJson);

        var reorderedPermissions = rig with
        {
            Agents = [rig.Agents[0] with
            {
                HarnessSettings = rig.Agents[0].HarnessSettings with
                {
                    Permissions = rig.Agents[0].HarnessSettings.Permissions with
                    {
                        Allow = rig.Agents[0].HarnessSettings.Permissions.Allow.Reverse().ToArray()
                    }
                }
            }, rig.Agents[1]]
        };
        Assert.NotEqual(RigCanonicalizer.Create(rig).SharedJson, RigCanonicalizer.Create(reorderedPermissions).SharedJson);

        var skill = rig.Agents[0].Skills[0] with
        {
            Files =
            [
                new EmbeddedFile("é", "sha256:two", [1, 2]),
                new EmbeddedFile("e\u0301", "sha256:one", [1])
            ]
        };
        var collisionRig = rig with
        {
            Agents = [rig.Agents[0] with { Skills = [skill] }, rig.Agents[1]]
        };
        using var document = JsonDocument.Parse(RigCanonicalizer.Create(collisionRig).SharedJson);
        var files = document.RootElement.GetProperty("agents")[0].GetProperty("skills")[0].GetProperty("files");
        Assert.Equal([1, 2], files.EnumerateArray().Select(item => item.GetProperty("bytes").GetInt32()));
        Assert.Equal("é", files[0].GetProperty("path").GetString());
        Assert.Equal("é", files[1].GetProperty("path").GetString());
    }

    [Fact]
    public void CANbindingWritesAndHashesOnlyTheBindingTree()
    {
        var rig = MinimalRig();
        var result = RigCanonicalizer.Create(rig);
        Assert.Equal(Golden("minimal.binding.json"), result.BindingJson);

        var changed = RigCanonicalizer.Create(rig with
        {
            Binding = rig.Binding with { Repos = [new ResolvedRepoBinding("app", "~/APP/")] }
        });

        Assert.NotEqual(result.BindingJson, changed.BindingJson);
        Assert.NotEqual(result.BindingHash, changed.BindingHash);
        Assert.Equal(result.SharedJson, changed.SharedJson);

        var bindingVariants = new[]
        {
            rig.Binding with { SeatRoot = "~/other/seats" },
            rig.Binding with { Placement = [new ResolvedPlacement("impl", "remote")] },
            rig.Binding with { Secrets = [new ResolvedSecretSource("other", "~/secret/other")] }
        };
        foreach (var binding in bindingVariants)
        {
            var variant = RigCanonicalizer.Create(rig with { Binding = binding });
            Assert.NotEqual(result.BindingJson, variant.BindingJson);
            Assert.NotEqual(result.BindingHash, variant.BindingHash);
            Assert.Equal(result.SharedJson, variant.SharedJson);
        }

        var complex = OrderingRig();
        using (var complexBinding = JsonDocument.Parse(RigCanonicalizer.Create(complex).BindingJson))
            Assert.Contains(complexBinding.RootElement.GetProperty("repos").EnumerateArray(),
                repo => repo.GetProperty("name").GetString() == "unused");

        var shuffledBindings = RigCanonicalizer.Create(complex with
        {
            Binding = complex.Binding with
            {
                Placement = complex.Binding.Placement.Reverse().ToArray(),
                Repos = complex.Binding.Repos.Reverse().ToArray(),
                Secrets = complex.Binding.Secrets.Reverse().ToArray()
            }
        });
        Assert.Equal(RigCanonicalizer.Create(complex).BindingJson, shuffledBindings.BindingJson);
    }

    [Fact]
    public void HASHminimalMatchesGoldenHashesAndResolvedJson()
    {
        var result = RigCanonicalizer.Create(MinimalRig());

        Assert.Equal(Golden("minimal.spec.sha256"), result.SpecHash);
        Assert.Equal(Golden("minimal.binding.sha256"), result.BindingHash);
        Assert.Equal($"{{\"binding\":{Golden("minimal.binding.json")},\"shared\":{Golden("minimal.shared.json")}}}",
            result.ResolvedJson);
        Assert.False(result.SharedJson.EndsWith('\n'));
        Assert.False(result.BindingJson.EndsWith('\n'));
        Assert.DoesNotContain('\ufeff', result.SharedJson);
    }

    [Fact]
    public void HASHcontentCopiesDistinctBytesAndDoesNotReopenOrShareMutableArrays()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canonical-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "culture.md");
        byte[] cultureBytes = [10, 13, 10];
        File.WriteAllBytes(filePath, cultureBytes);

        var agent = MinimalRig().Agents[0] with
        {
            Guidance = [new EmbeddedFile("agents/impl/guide.md", "sha256:a", [21, 22])],
            Skills =
            [
                new ResolvedSkill("agents/impl/skills/build", "build", "builds",
                [
                    new EmbeddedFile("agents/impl/skills/build/SKILL.md", "sha256:a", [21, 22]),
                    new EmbeddedFile("agents/impl/skills/build/.metadata", "sha256:c", [0, 255])
                ])
            ]
        };
        var rig = MinimalRig() with
        {
            Culture = new EmbeddedFile("culture.md", "sha256:b", File.ReadAllBytes(filePath)),
            Agents = [agent]
        };

        var first = RigCanonicalizer.Create(rig);
        var repeated = RigCanonicalizer.Create(rig);
        Assert.Equal(["sha256:a", "sha256:b", "sha256:c"], first.Contents.Keys);
        Assert.Equal([21, 22], first.Contents["sha256:a"]);
        Assert.Equal([10, 13, 10], first.Contents["sha256:b"]);
        Assert.Equal([0, 255], first.Contents["sha256:c"]);
        Assert.Equal(first.SharedJson, repeated.SharedJson);
        Assert.Equal(first.SpecHash, repeated.SpecHash);

        first.Contents["sha256:a"][0] = 99;
        Assert.Equal(21, rig.Agents[0].Guidance[0].Content[0]);
        rig.Agents[0].Guidance[0].Content[0] = 88;
        Assert.Equal(21, repeated.Contents["sha256:a"][0]);

        Directory.Delete(root, recursive: true);
        var afterDelete = RigCanonicalizer.Create(rig);
        Assert.Equal(repeated.SharedJson, afterDelete.SharedJson);
        Assert.Equal(repeated.SpecHash, afterDelete.SpecHash);
        Assert.Equal(repeated.BindingHash, afterDelete.BindingHash);
        Assert.Equal([10, 13, 10], afterDelete.Contents["sha256:b"]);
    }

    private static string Golden(string name) => File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", name))
        .TrimEnd('\r', '\n');

    private static ResolvedRig MinimalRig()
    {
        var settings = EmptySettings();
        var agent = new ResolvedAgent("agents/impl", "impl", "Implements issues", "claude-code", null,
            [], [], settings);
        var seat = new ResolvedAgentSeat("agents/impl", "claude-code", null, "shared", ["app"], "app",
            new ResolvedRequirements("optional", "subscription", []), settings);
        return new ResolvedRig("demo", "", null,
            [new ResolvedRepo("app", "https://github.com/example/app.git", "main")],
            [agent], [new ResolvedSeat("impl", "agent", "Implements issues", seat)],
            new ResolvedBinding("~/aiakos/seats", [new ResolvedPlacement("impl", "local")],
                [new ResolvedRepoBinding("app", "/home/dev/app")], []), []);
    }

    private static ResolvedRig OrderingRig()
    {
        var first = MinimalRig().Agents[0] with
        {
            Guidance =
            [
                new EmbeddedFile("agents/a/z.md", "sha256:z", [3]),
                new EmbeddedFile("agents/a/a.md", "sha256:a", [1])
            ],
            Skills =
            [
                new ResolvedSkill("agents/impl/skills/z-build", "z-build", "z builds",
                [
                    new EmbeddedFile("agents/impl/skills/z-build/z.md", "sha256:z-file", [3]),
                    new EmbeddedFile("agents/impl/skills/z-build/a.md", "sha256:a-file", [1])
                ]),
                new ResolvedSkill("agents/impl/skills/a-build", "a-build", "a builds", [])
            ],
            HarnessSettings = new ResolvedHarnessSettings("default",
                new ResolvedPermissions(["z", "a"], ["ask-2", "ask-1"], ["deny-2", "deny-1"]))
        };
        var second = first with
        {
            Directory = "agents/review",
            Name = "review",
            Description = "Reviews issues",
            Guidance = [],
            Skills = []
        };
        var firstSeat = MinimalRig().Seats[0] with
        {
            Agent = MinimalRig().Seats[0].Agent! with
            {
                Repos = ["lib", "app"],
                WorkdirRepo = "app",
                Requires = new ResolvedRequirements("optional", "subscription", ["z-secret", "a-secret"])
            }
        };
        var reviewSeat = new ResolvedSeat("review", "agent", "Reviews issues",
            firstSeat.Agent! with { AgentDirectory = "agents/review", Repos = ["app", "lib"] });
        var otherSeat = new ResolvedSeat("human", "human", "Maintains issues", null);
        return MinimalRig() with
        {
            Culture = new EmbeddedFile("culture.md", "sha256:culture", [5, 6, 7]),
            Repos =
            [
                new ResolvedRepo("unused", "https://example.test/unused.git", "main"),
                new ResolvedRepo("lib", "https://example.test/lib.git", "main"),
                new ResolvedRepo("app", "https://example.test/app.git", "main")
            ],
            Agents = [first, second],
            Seats = [firstSeat, otherSeat, reviewSeat],
            Binding = new ResolvedBinding("~/aiakos/seats",
                [new ResolvedPlacement("review", "node-b"), new ResolvedPlacement("impl", "node-a")],
                [new ResolvedRepoBinding("unused", "~/src/unused"), new ResolvedRepoBinding("lib", "~/src/lib/"),
                    new ResolvedRepoBinding("app", "/home/dev/app")],
                [new ResolvedSecretSource("z-secret", "~/secret/z"), new ResolvedSecretSource("a-secret", "~/secret/a")])
        };
    }

    private static ResolvedHarnessSettings EmptySettings() => new("default", new ResolvedPermissions([], [], []));
}
