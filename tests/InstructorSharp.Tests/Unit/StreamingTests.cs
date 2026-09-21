using InstructorSharp.Tests.Fakes;
using Xunit;

namespace InstructorSharp.Tests.Unit;

public class StreamingTests
{
    [Fact]
    public async Task Streaming_yields_progressively_more_complete_snapshots()
    {
        var client = new FakeChatClient()
            .RespondWith("""{"name":"Faseeh Ullah Jafar","age":29,"city":"Lahore"}""");

        var snapshots = new List<UserInfo>();
        await foreach (UserInfo snapshot in client.AsInstructor().StreamAsync<UserInfo>("who is this"))
        {
            snapshots.Add(snapshot);
        }

        Assert.True(snapshots.Count > 1, "expected more than one snapshot from a chunked stream");

        // The name fills in character by character as the chunks arrive.
        Assert.True(
            snapshots.Select(s => s.Name).Distinct().Count() > 1,
            "expected the name to fill in over successive snapshots");

        UserInfo final = snapshots[snapshots.Count - 1];
        Assert.Equal("Faseeh Ullah Jafar", final.Name);
        Assert.Equal(29, final.Age);
        Assert.Equal("Lahore", final.City);
    }

    [Fact]
    public async Task Snapshot_names_are_always_prefixes_of_the_final_value()
    {
        // A snapshot must never show something that is not on its way to being correct.
        var client = new FakeChatClient().RespondWith("""{"name":"Abdul Rehman","age":40}""");

        var names = new List<string>();
        await foreach (UserInfo snapshot in client.AsInstructor().StreamAsync<UserInfo>("who is this"))
        {
            names.Add(snapshot.Name);
        }

        Assert.All(names, n => Assert.StartsWith(n, "Abdul Rehman", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_final_streamed_object_is_validated()
    {
        var client = new FakeChatClient().RespondWith("""{"name":"Ali","age":500}""");

        await Assert.ThrowsAsync<ExtractionFailedException>(async () =>
        {
            await foreach (UserInfo _ in client.AsInstructor().StreamAsync<UserInfo>("who is Ali"))
            {
            }
        });
    }

    [Fact]
    public async Task A_stream_that_never_produces_json_fails_loudly()
    {
        var client = new FakeChatClient().RespondWith("I cannot help with that request.");

        await Assert.ThrowsAsync<ExtractionFailedException>(async () =>
        {
            await foreach (UserInfo _ in client.AsInstructor().StreamAsync<UserInfo>("who is Ali"))
            {
            }
        });
    }

    [Fact]
    public async Task StreamList_yields_elements_as_they_complete()
    {
        var client = new FakeChatClient().RespondWith(
            """{"value":[{"name":"Ali","age":29},{"name":"Sara","age":31},{"name":"Bilal","age":45}]}""");

        var people = new List<UserInfo>();
        await foreach (UserInfo person in client.AsInstructor().StreamListAsync<UserInfo>("list the people"))
        {
            people.Add(person);
        }

        Assert.Equal(3, people.Count);
        Assert.Equal(["Ali", "Sara", "Bilal"], people.Select(p => p.Name));
    }

    [Fact]
    public async Task StreamList_never_yields_a_half_written_element()
    {
        // Elements are only handed out once a later element proves them settled, so a consumer
        // never sees a record with a truncated name.
        var client = new FakeChatClient().RespondWith(
            """{"value":[{"name":"Alexander","age":29},{"name":"Bartholomew","age":31}]}""");

        var people = new List<UserInfo>();
        await foreach (UserInfo person in client.AsInstructor().StreamListAsync<UserInfo>("list them"))
        {
            people.Add(person);
        }

        Assert.All(people, p => Assert.Contains(p.Name, new[] { "Alexander", "Bartholomew" }));
    }

    [Fact]
    public async Task StreamList_handles_an_empty_collection()
    {
        var client = new FakeChatClient().RespondWith("""{"value":[]}""");

        var people = new List<UserInfo>();
        await foreach (UserInfo person in client.AsInstructor().StreamListAsync<UserInfo>("list them"))
        {
            people.Add(person);
        }

        Assert.Empty(people);
    }

    [Fact]
    public async Task Streaming_can_be_abandoned_early()
    {
        var client = new FakeChatClient().RespondWith("""{"name":"Ali","age":29,"city":"Lahore"}""");

        int seen = 0;
        await foreach (UserInfo _ in client.AsInstructor().StreamAsync<UserInfo>("who is Ali"))
        {
            seen++;
            if (seen == 1)
            {
                break;
            }
        }

        Assert.Equal(1, seen);
    }
}
