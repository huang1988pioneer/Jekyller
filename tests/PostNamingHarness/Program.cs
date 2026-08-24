using Jekyller.Services;

var date = new DateTime(2026, 8, 23);
Assert(PostNaming.NextDefaultTitle([], date) == "text20260823-1", "first of the day");
Assert(
    PostNaming.NextDefaultTitle(["2026-08-23-hello", "text20260823-1", "2026-08-23-text20260823-2"], date)
    == "text20260823-3",
    "skips used numbers from titles and filenames");
Assert(
    PostNaming.NextDefaultTitle(["text20260822-9"], date) == "text20260823-1",
    "other days do not count");
Assert(PostNaming.IsDefaultTitle("text20260823-1"), "generated title");
Assert(!PostNaming.IsDefaultTitle("我的文章"), "custom title");

Console.WriteLine("POST_NAMING_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
