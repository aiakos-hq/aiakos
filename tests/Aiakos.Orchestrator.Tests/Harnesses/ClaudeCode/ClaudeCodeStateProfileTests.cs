using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Harnesses;
using Aiakos.Orchestrator.Harnesses.ClaudeCode;
using Aiakos.Orchestrator.Seats;
using Xunit;

namespace Aiakos.Orchestrator.Tests.Harnesses.ClaudeCode;

public sealed class ClaudeCodeStateProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    private const string NativeId = "abcdefab-cdef-4abc-8def-abcdefabcdef";
    private const string RotatedId = "22222222-2222-4222-8222-222222222222";

    [Fact]
    public void ProfileHasExactIdentityTimeoutsAndStrictGeneratedIds()
    {
        var profile = new ClaudeCodeStateProfile();

        Assert.Equal("claude-code", profile.Harness);
        Assert.True(profile.FreshRelaunchReusesSessionId);
        Assert.False(profile.EmitsInputResolved);
        Assert.Equal(TimeSpan.FromSeconds(15), profile.ReadyTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), profile.ConfirmTimeout);
        Assert.Equal(TimeSpan.FromMinutes(10), profile.QuietTimeout);

        for (var index = 0; index < 128; index++)
        {
            var id = profile.NewNativeSessionId();
            Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", id);
            Assert.True(profile.IsValidNativeSessionId(id));
        }

        Assert.True(profile.IsValidNativeSessionId(NativeId));
        Assert.False(profile.IsValidNativeSessionId(NativeId.ToUpperInvariant()));
        Assert.False(profile.IsValidNativeSessionId("11111111-1111-1111-8111-111111111111"));
        Assert.False(profile.IsValidNativeSessionId("11111111-1111-4111-0111-111111111111"));
        Assert.False(profile.IsValidNativeSessionId($" {NativeId}"));
        Assert.False(profile.IsValidNativeSessionId($"{NativeId} "));
        Assert.False(profile.IsValidNativeSessionId(string.Empty));
        Assert.False(profile.IsValidNativeSessionId(null!));
    }

    [Fact]
    public void NeutralHarnessContractsExposeTheSpecifiedMembers()
    {
        Assert.Equal(new NativeSession(NativeId), new NativeSession(NativeId));
        Assert.Equal(typeof(Aiakos.Core.IHarnessAdapter), Assert.Single(typeof(IHarnessAdapter).GetInterfaces()));
        AssertMethod(typeof(IHarnessAdapter), "BuildLaunch", typeof(LaunchSpec), typeof(Aiakos.Spec.ResolvedSeatParameters),
            typeof(LaunchMode), typeof(NativeSession), typeof(IReadOnlyList<SeatFile>));
        AssertMethod(typeof(IHarnessAdapter), "BuildDelivery", typeof(DeliverySpec), typeof(string), typeof(string), typeof(Guid));
        AssertProperties(typeof(NativeSession), "Id");
        AssertProperties(typeof(LaunchSpec), "Argv", "Env", "Files", "ReadyTimeout", "Terminal");
        AssertProperties(typeof(DeliverySpec), "Lead", "Body", "ExpectConfirmation", "ConfirmTimeout");
    }

    [Fact]
    public void ProfileClassifiesEveryHarnessKindAndSourceExactly()
    {
        var profile = new ClaudeCodeStateProfile();
        var sources = new string?[] { "startup", "resume", "fork", "clear", "Startup", "unknown", null };
        var conversationKinds = new[]
        {
            HarnessEventKind.PromptSubmitted,
            HarnessEventKind.TurnEnded,
            HarnessEventKind.TurnFailed
        };

        foreach (var kind in Enum.GetValues<HarnessEventKind>())
        foreach (var source in sources)
        {
            IReadOnlyDictionary<string, string> attributes = source is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = source };

            var expectedReady = kind == HarnessEventKind.SessionStarted &&
                source is "startup" or "resume" or "fork" or "clear";
            Assert.Equal(expectedReady, profile.IsReadiness(kind, attributes));
            Assert.Equal(conversationKinds.Contains(kind), profile.IsConversationEvidence(kind, attributes));

            attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = source ?? string.Empty,
                ["previous_session_id"] = NativeId
            };
            Assert.Equal(kind == HarnessEventKind.SessionStarted && source == "clear",
                profile.IsSessionRotation(kind, attributes));
        }

        Assert.False(profile.IsReadiness(HarnessEventKind.SessionStarted, null!));
        Assert.True(profile.IsConversationEvidence(HarnessEventKind.PromptSubmitted, null!));
        Assert.False(profile.IsSessionRotation(HarnessEventKind.SessionStarted, null!));
        Assert.False(profile.IsSessionRotation(HarnessEventKind.SessionStarted,
            new Dictionary<string, string> { ["source"] = "clear" }));
        Assert.False(profile.IsSessionRotation(HarnessEventKind.SessionStarted,
            new Dictionary<string, string> { ["source"] = "clear", ["previous_session_id"] = NativeId.ToUpperInvariant() }));
        Assert.False(profile.IsSessionRotation(HarnessEventKind.SessionStarted,
            new Dictionary<string, string> { ["source"] = "clear", ["previous_session_id"] = "invalid" }));
        var ignoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SOURCE"] = "clear",
            ["PREVIOUS_SESSION_ID"] = NativeId
        };
        Assert.False(profile.IsReadiness(HarnessEventKind.SessionStarted, ignoreCase));
        Assert.False(profile.IsSessionRotation(HarnessEventKind.SessionStarted, ignoreCase));
    }

    [Fact]
    public void ProfileIsPureAndStateMachineKeepsCallerInChargeOfNativeSessionMatching()
    {
        var profile = new ClaudeCodeStateProfile();
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = "clear",
            ["previous_session_id"] = NativeId,
            ["opaque"] = "unicode-\uFFFE-\U0001F680"
        };
        var original = new Dictionary<string, string>(attributes, StringComparer.Ordinal);

        var initial = State(NativeId) with { ReadinessSeen = false };
        var ready = SeatStateMachine.Apply(initial,
            Harness(HarnessEventKind.SessionStarted, NativeId,
                new Dictionary<string, string> { ["source"] = "startup" }), profile, Now);
        Assert.True(ready.State.ReadinessSeen);
        Assert.Equal(NativeId, ready.State.NativeSessionId);

        var rotate = SeatStateMachine.Apply(ready.State,
            Harness(HarnessEventKind.SessionStarted, RotatedId, attributes, 2, 2), profile, Now);
        Assert.Equal(RotatedId, rotate.State.NativeSessionId);
        Assert.Contains(rotate.Effects, effect => effect == new AdoptRotatedSession(RotatedId, NativeId));

        var freshOnly = rotate.State with { Resumability = ResumabilityValue.FreshOnly };
        var wrongSession = SeatStateMachine.Apply(freshOnly,
            Harness(HarnessEventKind.PromptSubmitted, NativeId, new Dictionary<string, string>(), 3, 3), profile, Now);
        Assert.Equal(ResumabilityValue.FreshOnly, wrongSession.State.Resumability);

        var currentSession = SeatStateMachine.Apply(freshOnly,
            Harness(HarnessEventKind.PromptSubmitted, RotatedId, new Dictionary<string, string>(), 4, 4), profile, Now);
        Assert.Equal(ResumabilityValue.Resumable, currentSession.State.Resumability);
        Assert.Equal(original, attributes);
    }

    private static SeatState State(string nativeSessionId) => SeatState.Initial(Now) with
    {
        Session = SessionValue.Present,
        KnownSession = SessionValue.Present,
        SessionSince = Now,
        KnownSessionReason = null,
        SessionReason = null,
        Activity = ActivityValue.Working,
        KnownActivity = ActivityValue.Working,
        ActivitySince = Now,
        KnownActivityReason = null,
        ActivityReason = null,
        Desired = SeatDesired.Up,
        Resumability = ResumabilityValue.Resumable,
        NativeSessionId = nativeSessionId,
        ReadinessSeen = true,
        NodeInstanceId = Guid.Parse("33333333-3333-4333-8333-333333333333"),
        Launch = new CurrentLaunch(Guid.Parse("44444444-4444-4444-8444-444444444444"), LaunchMode.Fresh, false, false)
    };

    private static EventReceived Harness(HarnessEventKind kind, string nativeSessionId,
        IReadOnlyDictionary<string, string> attributes, long seq = 1, long sourceSeq = 1) => new(
        Guid.Parse("33333333-3333-4333-8333-333333333333"), seq, sourceSeq,
        Guid.Parse("44444444-4444-4444-8444-444444444444"),
        new HarnessBody(kind, nativeSessionId, attributes));

    private static void AssertMethod(Type type, string name, Type returnType, params Type[] parameterTypes)
    {
        var method = type.GetMethod(name)!;
        Assert.Equal(returnType, method.ReturnType);
        Assert.Equal(parameterTypes, method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void AssertProperties(Type type, params string[] names)
    {
        Assert.Equal(names.Order(), type.GetProperties().Select(property => property.Name).Order());
        Assert.All(type.GetProperties(), property =>
            Assert.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit),
                property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()));
    }
}
