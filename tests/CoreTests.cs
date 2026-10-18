using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using TaskBoard.Core;
using TaskBoard.Persistence;

internal static class CoreTests
{
    private static int failures;

    private static int Main()
    {
        Run("ISO week boundaries and DateTime limits", DateBoundaries);
        Run("ISO calendar agrees across a Gregorian 400-year cycle", CompleteCalendarCycle);
        Run("Remaining days include today and handle leap years", RemainingDays);
        Run("Unicode, newlines, color and order survive persistence", RoundTrip);
        Run("Corrupt primary is preserved and the backup is recovered", BackupRecovery);
        Run("Both corrupt files are preserved before a fresh board is saved", BothFilesCorrupt);
        Run("Trailing JSON content is rejected and preserved", TrailingContent);
        Run("Invalid fields are normalized without mutating UI state", Normalization);
        Run("Legacy Task column migrates to Monday without changing existing days", LegacyColumnMigration);
        Run("Failed replacement leaves the old board and no temporary files", FailedSave);
        Run("Unreadable originals cannot be overwritten", LockedOriginal);
        Run("Corruption between saves is preserved before replacement", ExternalCorruption);
        Run("Daily application retains source and creates one last item per day", DailyApplication);
        Run("Daily application is idempotent and repairs missing or duplicate copies", DailyApplicationRepair);
        Run("Daily edits sync their group and cloning preserves membership", DailySyncAndClone);
        Run("Task arrangement preserves the mixed pending order and bottom additions", DailyArrangement);
        Run("Completed tasks sort first with stable pending ordinary and routine order", CompletionArrangement);
        Run("Routine completion is independent per day and survives reapplication", DailyCompletionIndependence);
        Run("Completion flags survive JSON and cloning with false legacy defaults", CompletionPersistence);
        Run("Weekly legacy migration uses current Monday across year boundaries", WeeklyLegacyMigration);
        Run("Week switching preserves archives, replaced task lists and startup current week", WeeklySelectionPersistence);
        Run("Weekly save snapshots exclude drafts from both active task mirrors", WeeklyDraftSnapshots);
        Run("Future weeks carry daily groups with new IDs and independent completion", WeeklyDailyCarry);
        Run("Unknown past weeks remain blank and saved history remains independent", WeeklyPastHistory);
        Run("Weekly metadata is canonicalized, duplicates merged and corruption preserved", WeeklyMetadataValidation);
        Run("Clearing a day removes only active-day tasks and preserves sibling routines and archives", ClearDayIsolation);
        Run("Unknown legacy data survives normalization, weekly snapshots and repeated saves", LegacyUnknownFieldsRoundTrip);
        Run("Retired empty and null fields remain opaque while fresh boards add no retired defaults", LegacyEmptyUnknownFields);
        Run("Task backgrounds migrate, validate, roundtrip and clone independently", BackgroundPersistence);
        Run("Daily application and edits preserve independent task backgrounds", BackgroundDailyIsolation);
        Run("Future weeks retain weekday backgrounds and draft-safe archive snapshots", BackgroundWeeklyCarry);
        Run("Legacy tasks differing only in background are not lost during merge", BackgroundLegacyMerge);
        Run("Unfinished rollover waits for Monday and handles the ISO year boundary", RolloverBoundary);
        Run("Rollover appends in chronological visible order and is idempotent", RolloverOrder);
        Run("Missed daily occurrences remain independent while the new week is seeded", RolloverDailyOccurrences);
        Run("Week browsing does not roll tasks forward before their actual expiry", RolloverNavigationIsolation);
        Run("Skipped weeks and backward clocks preserve only existing work", RolloverSkippedWeeks);
        Run("Rollover commits replaced active lists and preserves the viewed week", RolloverReplacedActiveList);
        Run("Rollover survives atomic saves, reloads and opaque legacy fields", RolloverPersistence);
        Run("Manual next-week moves preserve task identity, styles and other plans", ManualNextWeekMove);
        Run("Manual daily moves seed first and detach only the selected occurrence", ManualDailyMove);
        Run("Manual next-week moves reject missing tasks and unrepresentable Sundays", ManualMoveLimits);
        Run("Inbox persists globally with Unicode, styles, completion and opaque legacy data", InboxPersistence);
        Run("Inbox normalization repairs groups and duplicate IDs without saving blank drafts", InboxNormalization);
        Run("Inbox snapshots are independent and week browsing leaves collected tasks intact", InboxSnapshotIsolation);
        Run("Inbox scheduling appends to the selected week and preserves other weeks", InboxScheduleIsolation);
        Run("Moving a daily occurrence to the inbox preserves peers and completion on roundtrip", InboxDailyRoundTrip);
        Run("Inbox transfers avoid ID collisions and reject unavailable or empty tasks", InboxTransferValidation);
        Console.WriteLine(failures == 0 ? "All core checks passed." : failures + " check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + error);
        }
    }

    private static void DateBoundaries()
    {
        AssertWeek(new DateTime(2021, 1, 1), 2020, 53);
        AssertWeek(new DateTime(2021, 1, 4), 2021, 1);
        AssertWeek(new DateTime(2018, 12, 31), 2019, 1);
        AssertWeek(new DateTime(2017, 1, 1), 2016, 52);
        AssertWeek(new DateTime(2026, 10, 15), 2026, 42);
        AssertWeek(DateTime.MinValue, 1, 1);
        AssertWeek(DateTime.MaxValue, 9999, 52);
    }

    private static void CompleteCalendarCycle()
    {
        // Independent reference: ISO week one starts on the Monday containing Jan 4.
        for (int year = 2000; year < 2400; year++)
        {
            DateTime firstMonday = FirstMonday(year);
            DateTime nextFirstMonday = FirstMonday(year + 1);
            for (DateTime date = new DateTime(year, 1, 1); date.Year == year; date = date.AddDays(1))
            {
                int weekYear = year;
                DateTime start = firstMonday;
                if (date < firstMonday)
                {
                    weekYear--;
                    start = FirstMonday(weekYear);
                }
                else if (date >= nextFirstMonday)
                {
                    weekYear++;
                    start = nextFirstMonday;
                }
                int number = (int)(date - start).TotalDays / 7 + 1;
                AssertWeek(date, weekYear, number);
            }
        }
    }

    private static DateTime FirstMonday(int year)
    {
        DateTime januaryFourth = new DateTime(year, 1, 4);
        return januaryFourth.AddDays(-(((int)januaryFourth.DayOfWeek + 6) % 7));
    }

    private static void AssertWeek(DateTime date, int year, int number)
    {
        DateMetrics metrics = DateMetrics.From(date);
        Equal(year, metrics.WeekYear, "ISO year for " + date.ToString("yyyy-MM-dd"));
        Equal(number, metrics.WeekNumber, "ISO week for " + date.ToString("yyyy-MM-dd"));
    }

    private static void RemainingDays()
    {
        DateMetrics first = DateMetrics.From(new DateTime(2026, 1, 1, 23, 59, 59));
        Near(100, first.MonthRemainingPercent);
        Near(100, first.YearRemainingPercent);
        DateMetrics leapDay = DateMetrics.From(new DateTime(2024, 2, 29));
        Near(100.0 / 29, leapDay.MonthRemainingPercent);
        Near(30700.0 / 366, leapDay.YearRemainingPercent);
        DateMetrics leapDayEve = DateMetrics.From(new DateTime(2024, 2, 28));
        Near(200.0 / 29, leapDayEve.MonthRemainingPercent);
        DateMetrics final = DateMetrics.From(new DateTime(2026, 12, 31, 23, 59, 59));
        Near(100.0 / 31, final.MonthRemainingPercent);
        Near(100.0 / 365, final.YearRemainingPercent);
        DateMetrics current = DateMetrics.From(new DateTime(2026, 10, 15));
        Near(1700.0 / 31, current.MonthRemainingPercent);
        Near(7800.0 / 365, current.YearRemainingPercent);
        DateMetrics midnight = DateMetrics.From(new DateTime(2026, 10, 15, 0, 0, 0));
        DateMetrics evening = DateMetrics.From(new DateTime(2026, 10, 15, 23, 59, 59));
        Near(midnight.MonthRemainingPercent, evening.MonthRemainingPercent);
        Near(midnight.YearRemainingPercent, evening.YearRemainingPercent);
    }

    private static BoardState Sample()
    {
        return new BoardState
        {
            Tasks = new List<TaskItem>
            {
                new TaskItem { Id = "a", Text = "中文 Task\n第二行\t\"quoted\" \\ path 😀", Color = "#A1B2C3", Column = 1 },
                new TaskItem { Id = "b", Text = "Sunday", Color = "#000000", Column = 7 },
                new TaskItem { Id = "c", Text = "Monday second", Color = "#DE3456", Column = 1 },
                new TaskItem { Id = "d", Text = "Friday", Color = "#111111", Column = 5 }
            }
        };
    }

    private static void RoundTrip()
    {
        InDirectory(delegate(string directory)
        {
            var store = new BoardStore(directory);
            BoardState original = Sample();
            store.Save(original);
            BoardState actual = store.Load();
            Equal(string.Empty, store.LastWarning, "healthy load warning");
            Equal(original.Tasks.Count, actual.Tasks.Count, "count");
            for (int i = 0; i < original.Tasks.Count; i++)
            {
                Equal(original.Tasks[i].Id, actual.Tasks[i].Id, "id");
                Equal(original.Tasks[i].Text, actual.Tasks[i].Text, "text");
                Equal(original.Tasks[i].Color, actual.Tasks[i].Color, "color");
                Equal(original.Tasks[i].Column, actual.Tasks[i].Column, "column");
            }
            Equal("a,c", string.Join(",", actual.Tasks.Where(t => t.Column == 1).Select(t => t.Id)), "column order");
            Assert(File.Exists(Path.Combine(directory, "board.backup.json")), "first save creates backup");
        });
    }

    private static void BackupRecovery()
    {
        InDirectory(delegate(string directory)
        {
            var store = new BoardStore(directory);
            BoardState state = Sample();
            store.Save(state);
            state.Tasks[0].Text = "new state";
            store.Save(state);
            const string corrupt = "malformed source content\n{[";
            File.WriteAllText(Path.Combine(directory, "board.json"), corrupt);
            BoardState recovered = new BoardStore(directory).Load();
            Equal(Sample().Tasks[0].Text, recovered.Tasks[0].Text, "previous valid snapshot recovered");
            string[] originals = Directory.GetFiles(directory, "board.corrupt-*.json");
            Equal(1, originals.Length, "preserved corrupt primary count");
            Equal(corrupt, File.ReadAllText(originals[0]), "corrupt bytes preserved");
            store = new BoardStore(directory);
            recovered = store.Load();
            Assert(store.LastWarning.Length > 0, "backup recovery warning");
            store.Save(recovered);
            Equal(recovered.Tasks[0].Text, store.Load().Tasks[0].Text, "saved recovery loads");
        });
    }

    private static void BothFilesCorrupt()
    {
        InDirectory(delegate(string directory)
        {
            File.WriteAllText(Path.Combine(directory, "board.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "board.backup.json"), "broken backup");
            var store = new BoardStore(directory);
            Equal(0, store.Load().Tasks.Count, "fresh empty board");
            Assert(store.LastWarning.Length > 0, "corruption warning");
            string[] preserved = Directory.GetFiles(directory, "*.corrupt-*.json");
            Equal(2, preserved.Length, "both originals preserved");
            Assert(preserved.Any(p => File.ReadAllText(p) == "{}"), "primary preserved exactly");
            Assert(preserved.Any(p => File.ReadAllText(p) == "broken backup"), "backup preserved exactly");
            store.Save(Sample());
            Equal(4, store.Load().Tasks.Count, "fresh save after corrupt recovery");
        });
    }

    private static void TrailingContent()
    {
        InDirectory(delegate(string directory)
        {
            var store = new BoardStore(directory);
            store.Save(Sample());
            File.AppendAllText(Path.Combine(directory, "board.json"), "\n{}");
            Equal(4, store.Load().Tasks.Count, "backup after trailing content");
            Equal(1, Directory.GetFiles(directory, "board.corrupt-*.json").Length, "trailing content preserved");
        });
    }

    private static void Normalization()
    {
        InDirectory(delegate(string directory)
        {
            var store = new BoardStore(directory);
            BoardState state = Sample();
            state.Tasks[0].Color = "#a1b2c3";
            state.Tasks[1].Color = "invalid";
            state.Tasks[1].Column = 999;
            state.Tasks[1].Id = state.Tasks[0].Id;
            state.Tasks[2].Text = null;
            store.Save(state);
            BoardState actual = store.Load();
            Equal("#A1B2C3", actual.Tasks[0].Color, "normalized hex");
            Equal("#111111", actual.Tasks[1].Color, "fallback hex");
            Equal(1, actual.Tasks[1].Column, "fallback column is Monday");
            Equal(string.Empty, actual.Tasks[2].Text, "fallback text");
            Equal(actual.Tasks.Count, actual.Tasks.Select(t => t.Id).Distinct().Count(), "unique IDs");
            Equal("#a1b2c3", state.Tasks[0].Color, "original color untouched");
            Equal(999, state.Tasks[1].Column, "original column untouched");
        });
    }

    private static void LegacyColumnMigration()
    {
        InDirectory(delegate(string directory)
        {
            Equal(1, new TaskItem().Column, "new task defaults to Monday");
            const string legacyJson = "{\"tasks\":[" +
                "{\"id\":\"legacy-a\",\"text\":\"原 Task 第一项\",\"color\":\"#A1B2C3\",\"column\":0}," +
                "{\"id\":\"sunday\",\"text\":\"周日\",\"color\":\"#123456\",\"column\":7}," +
                "{\"id\":\"monday-a\",\"text\":\"原周一第一项\",\"color\":\"#654321\",\"column\":1}," +
                "{\"id\":\"legacy-b\",\"text\":\"原 Task 第二项\",\"color\":\"#FEDCBA\",\"column\":0}," +
                "{\"id\":\"monday-b\",\"text\":\"原周一第二项\",\"color\":\"#111111\",\"column\":1}]," +
                "\"windowWidth\":1500,\"windowHeight\":900}";
            File.WriteAllText(Path.Combine(directory, "board.json"), legacyJson);
            var store = new BoardStore(directory);
            BoardState migrated = store.Load();
            Equal(string.Empty, store.LastWarning, "legacy data remains valid");
            Equal(5, migrated.Tasks.Count, "all legacy tasks retained");
            Equal("legacy-a,sunday,monday-a,legacy-b,monday-b", string.Join(",", migrated.Tasks.Select(t => t.Id)), "global order retained");
            Equal("legacy-a,monday-a,legacy-b,monday-b", string.Join(",", migrated.Tasks.Where(t => t.Column == 1).Select(t => t.Id)), "merged Monday order retained");
            Equal("原 Task 第一项", migrated.Tasks[0].Text, "first legacy task text retained");
            Equal("#A1B2C3", migrated.Tasks[0].Color, "first legacy task color retained");
            Equal("原 Task 第二项", migrated.Tasks[3].Text, "second legacy task text retained");
            Equal("#FEDCBA", migrated.Tasks[3].Color, "second legacy task color retained");
            Equal(1, migrated.Tasks[2].Column, "existing Monday unchanged");
            Equal(1, migrated.Tasks[4].Column, "second existing Monday unchanged");
            Equal(7, migrated.Tasks[1].Column, "Sunday unchanged");
            store.Save(migrated);
            BoardState roundTrip = store.Load();
            for (int i = 0; i < migrated.Tasks.Count; i++)
            {
                Equal(migrated.Tasks[i].Id, roundTrip.Tasks[i].Id, "migrated ID roundtrip");
                Equal(migrated.Tasks[i].Text, roundTrip.Tasks[i].Text, "migrated text roundtrip");
                Equal(migrated.Tasks[i].Color, roundTrip.Tasks[i].Color, "migrated color roundtrip");
                Equal(migrated.Tasks[i].Column, roundTrip.Tasks[i].Column, "migrated day roundtrip");
            }
        });
    }

    private static void DailyApplication()
    {
        BoardState state = Sample();
        TaskItem source = state.Tasks[0];
        state.Tasks.Add(new TaskItem { Id = "other-daily-a", Text = "Existing routine", Color = "#123456", Column = 1, DailyGroupId = "other" });
        state.Tasks.Add(new TaskItem { Id = "other-daily-b", Text = "Existing routine", Color = "#123456", Column = 7, DailyGroupId = "other" });
        BoardOperations.ApplyToEveryDay(state, source);
        Assert(source.IsDaily, "source becomes a daily task");
        Equal(12, state.Tasks.Count, "only six new copies added");
        Equal(1, state.Tasks.Count(t => t.Id == source.Id), "original source ID remains unique");
        Assert(state.Tasks.Contains(source), "original source object retained");
        for (int day = 1; day <= 7; day++)
        {
            List<TaskItem> dayTasks = state.Tasks.Where(t => t.Column == day).ToList();
            Equal(1, dayTasks.Count(t => t.DailyGroupId == source.DailyGroupId), "one group member in each day");
            Equal(source.DailyGroupId, dayTasks.Last().DailyGroupId, "applied routine is last in each day");
            Equal(source.Text, dayTasks.Last().Text, "daily text copied");
            Equal(source.Color, dayTasks.Last().Color, "daily color copied");
        }
        Equal("c,other-daily-a,a", string.Join(",", state.Tasks.Where(t => t.Column == 1).Select(t => t.Id)), "other Monday task order retained");
        Equal("b,other-daily-b", string.Join(",", state.Tasks.Where(t => t.Column == 7 && t.DailyGroupId != source.DailyGroupId).Select(t => t.Id)), "other Sunday task order retained");
    }

    private static void DailyApplicationRepair()
    {
        BoardState state = Sample();
        TaskItem source = state.Tasks[0];
        BoardOperations.ApplyToEveryDay(state, source);
        string firstOrder = string.Join(",", state.Tasks.Select(t => t.Id));
        var originalMembers = state.Tasks.Where(t => t.DailyGroupId == source.DailyGroupId).ToDictionary(t => t.Column, t => t.Id);
        TaskItem selectedCopy = state.Tasks.Single(t => t.Column == 4 && t.DailyGroupId == source.DailyGroupId);
        BoardOperations.ApplyToEveryDay(state, selectedCopy);
        Equal(firstOrder, string.Join(",", state.Tasks.Select(t => t.Id)), "repeated apply preserves IDs and order");
        TaskItem missing = state.Tasks.Single(t => t.Column == 3 && t.DailyGroupId == source.DailyGroupId);
        state.Tasks.Remove(missing);
        state.Tasks.Add(new TaskItem { Id = "duplicate", Text = "duplicate", Column = 4, DailyGroupId = source.DailyGroupId });
        BoardOperations.ApplyToEveryDay(state, selectedCopy);
        Equal(7, state.Tasks.Count(t => t.DailyGroupId == source.DailyGroupId), "repairs to exactly seven members");
        for (int day = 1; day <= 7; day++)
        {
            TaskItem member = state.Tasks.Single(t => t.Column == day && t.DailyGroupId == source.DailyGroupId);
            if (day != 3) Equal(originalMembers[day], member.Id, "existing copy ID preserved");
            Equal(selectedCopy.Text, member.Text, "repaired copy text");
        }
        Assert(!state.Tasks.Any(t => t.Id == "duplicate"), "duplicate copy removed");
        Assert(state.Tasks.Contains(selectedCopy), "selected copy retained");
    }

    private static void DailySyncAndClone()
    {
        BoardState state = Sample();
        TaskItem source = state.Tasks[0];
        BoardOperations.ApplyToEveryDay(state, source);
        TaskItem normal = state.Tasks.Single(t => t.Id == "b");
        state.Tasks.Add(new TaskItem { Id = "unrelated", Text = "Other group", Color = "#111111", Column = 2, DailyGroupId = "different" });
        source.Text = "新的每日事项\n第二行";
        source.Color = "#ABCDEF";
        BoardOperations.SyncDailyTask(state, source);
        Assert(state.Tasks.Where(t => t.DailyGroupId == source.DailyGroupId).All(t => t.Text == source.Text && t.Color == source.Color), "all seven copies synchronized");
        Equal("Sunday", normal.Text, "normal task unaffected");
        Equal("Other group", state.Tasks.Single(t => t.Id == "unrelated").Text, "other group unaffected");
        List<TaskItem> clones = BoardOperations.CloneTasks(state.Tasks);
        Equal(state.Tasks.Count, clones.Count, "all tasks cloned");
        for (int i = 0; i < clones.Count; i++)
        {
            Equal(state.Tasks[i].Id, clones[i].Id, "clone ID");
            Equal(state.Tasks[i].DailyGroupId, clones[i].DailyGroupId, "clone daily membership");
            Assert(!ReferenceEquals(state.Tasks[i], clones[i]), "clone has independent object");
        }
        clones.Single(t => t.Id == source.Id).Text = "changed clone";
        Equal("新的每日事项\n第二行", source.Text, "clone mutation leaves original unchanged");
    }

    private static void DailyArrangement()
    {
        var state = new BoardState
        {
            Tasks = new List<TaskItem>
            {
                new TaskItem { Id = "daily-b", Column = 1, DailyGroupId = "b" },
                new TaskItem { Id = "tuesday-normal", Column = 2 },
                new TaskItem { Id = "normal-a", Column = 1 },
                new TaskItem { Id = "daily-a", Column = 1, DailyGroupId = "a" },
                new TaskItem { Id = "normal-b", Column = 1 },
                new TaskItem { Id = "tuesday-daily", Column = 2, DailyGroupId = "a" }
            }
        };
        BoardOperations.ArrangeTasks(state);
        const string expected = "daily-b,normal-a,daily-a,normal-b,tuesday-normal,tuesday-daily";
        Equal(expected, string.Join(",", state.Tasks.Select(t => t.Id)), "stable mixed pending order");
        BoardOperations.ArrangeTasks(state);
        Equal(expected, string.Join(",", state.Tasks.Select(t => t.Id)), "arrangement idempotent");
        state.Tasks.Add(new TaskItem { Id = "new-normal", Column = 1 });
        BoardOperations.ArrangeTasks(state);
        Equal("daily-b,normal-a,daily-a,normal-b,new-normal", string.Join(",", state.Tasks.Where(t => t.Column == 1).Select(t => t.Id)), "new normal task stays below existing routines");
    }

    private static void CompletionArrangement()
    {
        var state = new BoardState
        {
            Tasks = new List<TaskItem>
            {
                new TaskItem { Id = "pending-daily-a", Column = 1, DailyGroupId = "a" },
                new TaskItem { Id = "completed-normal-a", Column = 1, IsCompleted = true },
                new TaskItem { Id = "pending-normal-a", Column = 1 },
                new TaskItem { Id = "completed-daily", Column = 1, IsCompleted = true, DailyGroupId = "a" },
                new TaskItem { Id = "pending-daily-b", Column = 1, DailyGroupId = "b" },
                new TaskItem { Id = "pending-normal-b", Column = 1 },
                new TaskItem { Id = "completed-normal-b", Column = 1, IsCompleted = true },
                new TaskItem { Id = "tuesday-pending", Column = 2 },
                new TaskItem { Id = "tuesday-completed", Column = 2, IsCompleted = true }
            }
        };
        BoardOperations.ArrangeTasks(state);
        const string expected = "completed-normal-a,completed-daily,completed-normal-b,pending-daily-a,pending-normal-a,pending-daily-b,pending-normal-b,tuesday-completed,tuesday-pending";
        Equal(expected, string.Join(",", state.Tasks.Select(t => t.Id)), "completed first with stable mixed pending order");
        BoardOperations.ArrangeTasks(state);
        Equal(expected, string.Join(",", state.Tasks.Select(t => t.Id)), "completion partition is idempotent");
        TaskItem justCompleted = state.Tasks.Single(t => t.Id == "pending-normal-b");
        state.Tasks.Remove(justCompleted);
        justCompleted.IsCompleted = true;
        state.Tasks.Insert(0, justCompleted);
        BoardOperations.ArrangeTasks(state);
        Equal("pending-normal-b", state.Tasks.First(t => t.Column == 1).Id, "newly completed task can be promoted to top");
    }

    private static void DailyCompletionIndependence()
    {
        var source = new TaskItem { Id = "source", Text = "Daily", Color = "#123456", Column = 1, IsCompleted = true };
        var otherCompleted = new TaskItem { Id = "other-completed", Column = 1, IsCompleted = true };
        var state = new BoardState { Tasks = new List<TaskItem> { source, otherCompleted } };
        BoardOperations.ApplyToEveryDay(state, source);
        Equal(1, state.Tasks.Count(t => t.DailyGroupId == source.DailyGroupId && t.IsCompleted), "new copies start incomplete");
        Assert(source.IsCompleted, "source completion retained");
        Equal("source,other-completed", string.Join(",", state.Tasks.Where(t => t.Column == 1).Select(t => t.Id)), "source completed order retained");
        TaskItem wednesday = state.Tasks.Single(t => t.Column == 3 && t.DailyGroupId == source.DailyGroupId);
        wednesday.IsCompleted = true;
        wednesday.Text = "Edited daily task";
        wednesday.Color = "#ABCDEF";
        BoardOperations.SyncDailyTask(state, wednesday);
        Equal(2, state.Tasks.Count(t => t.DailyGroupId == source.DailyGroupId && t.IsCompleted), "sync does not copy completion flags");
        Assert(state.Tasks.Where(t => t.DailyGroupId == source.DailyGroupId).All(t => t.Text == wednesday.Text && t.Color == wednesday.Color), "text and color still sync");
        BoardOperations.ApplyToEveryDay(state, wednesday);
        Equal(2, state.Tasks.Count(t => t.DailyGroupId == source.DailyGroupId && t.IsCompleted), "reapply preserves existing per-day completion");
        Equal("source,other-completed", string.Join(",", state.Tasks.Where(t => t.Column == 1).Select(t => t.Id)), "reapply keeps completed relative order");
        TaskItem removed = state.Tasks.Single(t => t.Column == 1 && t.DailyGroupId == source.DailyGroupId);
        state.Tasks.Remove(removed);
        BoardOperations.ApplyToEveryDay(state, wednesday);
        TaskItem recreated = state.Tasks.Single(t => t.Column == 1 && t.DailyGroupId == wednesday.DailyGroupId);
        Assert(!recreated.IsCompleted, "recreated missing member starts incomplete");
        Assert(wednesday.IsCompleted, "selected completed copy remains completed");
        Equal(7, state.Tasks.Count(t => t.DailyGroupId == wednesday.DailyGroupId), "completion does not duplicate routine members");
    }

    private static void CompletionPersistence()
    {
        InDirectory(delegate(string directory)
        {
            BoardState state = Sample();
            TaskItem source = state.Tasks[0];
            source.IsCompleted = true;
            BoardOperations.ApplyToEveryDay(state, source);
            TaskItem tuesday = state.Tasks.Single(t => t.Column == 2 && t.DailyGroupId == source.DailyGroupId);
            tuesday.IsCompleted = true;
            List<TaskItem> clones = BoardOperations.CloneTasks(state.Tasks);
            Equal(2, clones.Count(t => t.IsCompleted), "clones preserve completion flags");
            clones.Single(t => t.Id == source.Id).IsCompleted = false;
            Assert(source.IsCompleted, "clone completion mutations are independent");
            var store = new BoardStore(directory);
            store.Save(state);
            BoardState loaded = store.Load();
            Equal(2, loaded.Tasks.Count(t => t.IsCompleted), "JSON preserves per-day completion");
            Assert(loaded.Tasks.Single(t => t.Id == source.Id).IsCompleted, "completed source roundtrip");
            Assert(!loaded.Tasks.Single(t => t.Column == 4 && t.DailyGroupId == source.DailyGroupId).IsCompleted, "incomplete daily copy roundtrip");
            File.WriteAllText(Path.Combine(directory, "board.json"), "{\"tasks\":[{\"id\":\"old\",\"text\":\"Legacy\",\"color\":\"#123456\",\"column\":1}]}");
            Assert(!store.Load().Tasks[0].IsCompleted, "missing legacy flag defaults false");
            Assert(!new TaskItem().IsCompleted, "new task defaults incomplete");
        });
    }

    private static void WeeklyLegacyMigration()
    {
        Equal(new DateTime(2020, 12, 28), WeeklyPlanner.MondayOf(new DateTime(2021, 1, 1, 23, 59, 59)), "Friday belongs to previous year's Monday");
        Equal(new DateTime(2018, 12, 31), WeeklyPlanner.MondayOf(new DateTime(2019, 1, 6)), "Sunday belongs to previous year's Monday");
        Equal(new DateTime(2021, 1, 4), WeeklyPlanner.MondayOf(new DateTime(2021, 1, 4)), "Monday is unchanged");
        Equal(DateTime.MinValue, WeeklyPlanner.MondayOf(DateTime.MinValue), "minimum date Monday");
        Equal(new DateTime(9999, 12, 27), WeeklyPlanner.MondayOf(DateTime.MaxValue), "maximum date Monday");
        InDirectory(delegate(string directory)
        {
            File.WriteAllText(Path.Combine(directory, "board.json"), "{\"tasks\":[{\"id\":\"legacy\",\"text\":\"旧版事项\",\"color\":\"#ABCDEF\",\"column\":2,\"isCompleted\":true}]}");
            var store = new BoardStore(directory);
            BoardState state = store.Load();
            Equal(0, state.Weeks.Count, "old JSON starts without archives");
            WeeklyPlanner.Initialize(state, new DateTime(2021, 1, 1));
            Equal("2020-12-28", state.ActiveWeekStart, "legacy tasks assigned to system current week");
            Equal(1, state.Weeks.Count, "one legacy week created");
            Equal("legacy", state.Tasks[0].Id, "legacy ID retained");
            Equal("旧版事项", state.Tasks[0].Text, "legacy text retained");
            Equal("#ABCDEF", state.Tasks[0].Color, "legacy color retained");
            Assert(state.Tasks[0].IsCompleted, "legacy completion retained");
            Assert(ReferenceEquals(state.Tasks, state.Weeks[0].Tasks), "active list points to archive plan");
            WeeklyPlanner.Initialize(state, new DateTime(2021, 1, 1));
            Equal(1, state.Weeks.Count, "reinitialization does not duplicate week or tasks");
        });
    }

    private static void WeeklySelectionPersistence()
    {
        InDirectory(delegate(string directory)
        {
            DateTime today = new DateTime(2026, 10, 15);
            BoardState state = Sample();
            WeeklyPlanner.Initialize(state, today);
            List<TaskItem> originalWeek = state.Tasks;
            WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
            Equal("2026-10-19", state.ActiveWeekStart, "future week selected");
            Equal(0, state.Tasks.Count, "ordinary tasks do not carry forward");
            state.Tasks.Add(new TaskItem { Id = "future", Text = "Future plan", Column = 4 });
            List<TaskItem> replaced = BoardOperations.CloneTasks(state.Tasks);
            replaced[0].Text = "After undo replaced the list";
            state.Tasks = replaced;
            WeeklyPlanner.SelectWeek(state, today, today);
            Assert(ReferenceEquals(originalWeek, state.Tasks), "switching back reuses original independent list");
            Equal(4, state.Tasks.Count, "original week retained");
            WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
            Assert(ReferenceEquals(replaced, state.Tasks), "list replacement committed on navigation");
            Equal("After undo replaced the list", state.Tasks[0].Text, "future replacement retained");
            var store = new BoardStore(directory);
            store.Save(WeeklyPlanner.SnapshotForSave(state, null));
            BoardState loaded = store.Load();
            Equal("2026-10-19", loaded.ActiveWeekStart, "store does not auto-select system week");
            WeeklyPlanner.Initialize(loaded, today);
            Equal("2026-10-12", loaded.ActiveWeekStart, "startup selects system current week");
            Equal(4, loaded.Tasks.Count, "startup retains original tasks");
            Equal(2, loaded.Weeks.Count, "startup preserves future archive");
            WeeklyPlanner.SelectWeek(loaded, today.AddDays(7), today);
            Equal("After undo replaced the list", loaded.Tasks[0].Text, "future survives save, reload and startup selection");
        });
    }

    private static void WeeklyDraftSnapshots()
    {
        InDirectory(delegate(string directory)
        {
            DateTime today = new DateTime(2026, 10, 15);
            BoardState state = Sample();
            WeeklyPlanner.Initialize(state, today);
            WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
            state.Tasks.Add(new TaskItem { Id = "future", Text = "Archived future", Column = 1 });
            WeeklyPlanner.SelectWeek(state, today, today);
            List<TaskItem> committed = BoardOperations.CloneTasks(state.Tasks);
            state.Tasks[0].Color = "#FFFFFF";
            state.Tasks.Add(new TaskItem { Id = "draft", Text = "", Column = 1 });
            BoardState snapshot = WeeklyPlanner.SnapshotForSave(state, committed);
            Equal(4, snapshot.Tasks.Count, "active mirror excludes blank draft");
            Assert(!snapshot.Tasks.Any(t => t.Id == "draft"), "draft is not in root task list");
            WeekPlan active = snapshot.Weeks.Single(w => w.WeekStart == snapshot.ActiveWeekStart);
            Assert(ReferenceEquals(snapshot.Tasks, active.Tasks), "both active mirrors use same committed list");
            Assert(!active.Tasks.Any(t => t.Id == "draft"), "draft is not duplicated in archive");
            Equal("#A1B2C3", snapshot.Tasks.Single(t => t.Id == "a").Color, "pending color excluded from snapshot");
            Equal(5, state.Tasks.Count, "snapshot leaves live draft intact");
            snapshot.Weeks.Single(w => w.WeekStart == "2026-10-19").Tasks[0].Text = "Snapshot-only change";
            Equal("Archived future", state.Weeks.Single(w => w.WeekStart == "2026-10-19").Tasks[0].Text, "inactive archives deeply cloned");
            var store = new BoardStore(directory);
            store.Save(snapshot);
            BoardState loaded = store.Load();
            Equal(4, loaded.Tasks.Count, "saved active tasks exclude draft");
            Equal(4, loaded.Weeks.Single(w => w.WeekStart == loaded.ActiveWeekStart).Tasks.Count, "saved active archive excludes draft");
            state.Tasks = new List<TaskItem>();
            BoardState emptyUndo = WeeklyPlanner.SnapshotForSave(state, null);
            Equal(0, emptyUndo.Weeks.Single(w => w.WeekStart == state.ActiveWeekStart).Tasks.Count, "empty replacement overrides stale active archive");
        });
    }

    private static void WeeklyDailyCarry()
    {
        DateTime today = new DateTime(2026, 10, 15);
        BoardState state = Sample();
        TaskItem routine = state.Tasks[0];
        BoardOperations.ApplyToEveryDay(state, routine);
        routine.IsCompleted = true;
        WeeklyPlanner.Initialize(state, today);
        List<TaskItem> current = state.Tasks;
        var oldIds = new HashSet<string>(current.Select(t => t.Id));
        WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
        Equal(7, state.Tasks.Count, "future seeds routines only");
        Equal(7, state.Tasks.Select(t => t.Column).Distinct().Count(), "one daily copy for each weekday");
        Assert(state.Tasks.All(t => t.IsDaily && !t.IsCompleted), "new week's routines start incomplete");
        Assert(state.Tasks.All(t => !oldIds.Contains(t.Id)), "all future routine copies have new IDs");
        Assert(state.Tasks.All(t => t.DailyGroupId == routine.DailyGroupId), "routine group identity carried");
        TaskItem future = state.Tasks[0];
        future.Text = "Future routine text";
        future.Color = "#123456";
        future.IsCompleted = true;
        BoardOperations.SyncDailyTask(state, future);
        Assert(current.Where(t => t.IsDaily).All(t => t.Text != future.Text), "editing future routine leaves history unchanged");
        Assert(routine.IsCompleted, "history completion remains unchanged");
        state.Tasks.Remove(state.Tasks.Single(t => t.Column == 3));
        state.Tasks.Add(new TaskItem { Text = "Future ordinary task", Column = 2 });
        WeeklyPlanner.SelectWeek(state, today.AddDays(14), today);
        Equal(7, state.Tasks.Count, "later week deduplicates group and restores seven copies");
        Assert(state.Tasks.All(t => t.Text == "Future routine text" && t.Color == "#123456" && !t.IsCompleted), "latest earlier week's routine content carried, completion reset");
        WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
        Equal(7, state.Tasks.Count, "saved future retains its deleted copy and ordinary task");
        Equal(1, state.Tasks.Count(t => t.IsCompleted), "saved future completion stays independent");
        Equal(6, state.Tasks.Count(t => t.IsDaily), "existing weeks are never automatically reseeded");
    }

    private static void WeeklyPastHistory()
    {
        DateTime today = new DateTime(2026, 10, 15);
        var state = new BoardState();
        var routine = new TaskItem { Text = "Current routine", Column = 1 };
        state.Tasks.Add(routine);
        BoardOperations.ApplyToEveryDay(state, routine);
        WeeklyPlanner.Initialize(state, today);
        WeeklyPlanner.SelectWeek(state, today.AddDays(-7), today);
        Equal(0, state.Tasks.Count, "unknown past week receives no invented routines");
        state.Tasks.Add(new TaskItem { Id = "past", Text = "Actual saved history", Column = 5, IsCompleted = true });
        WeeklyPlanner.SelectWeek(state, today.AddDays(-14), today);
        Equal(0, state.Tasks.Count, "earlier unknown history remains blank");
        WeeklyPlanner.SelectWeek(state, today.AddDays(-7), today);
        Equal("Actual saved history", state.Tasks.Single().Text, "known past tasks retained");
        Assert(state.Tasks.Single().IsCompleted, "known past completion retained");
        WeeklyPlanner.SelectWeek(state, today, today);
        Equal(7, state.Tasks.Count, "current routines remain independent of history");
        WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
        Equal(7, state.Tasks.Count, "future uses current plan instead of blank older history");
    }

    private static void RolloverBoundary()
    {
        var state = Sample();
        WeeklyPlanner.Initialize(state, new DateTime(2021, 1, 3, 23, 59, 59));
        state.Tasks.Single(task => task.Id == "a").IsCompleted = true;
        Equal(0, WeeklyPlanner.RollOverUnfinished(state, new DateTime(2021, 1, 3, 23, 59, 59)), "Sunday is still the source week");
        Equal(3, WeeklyPlanner.RollOverUnfinished(state, new DateTime(2021, 1, 4)), "Monday rolls only pending tasks");
        Equal("2020-12-28", state.ActiveWeekStart, "core operation preserves the selected week");
        Equal("a", state.Tasks.Single().Id, "completed task remains in its original week");
        WeekPlan next = state.Weeks.Single(plan => plan.WeekStart == "2021-01-04");
        Equal("c,d,b", string.Join(",", next.Tasks.Select(task => task.Id)), "carry order follows Monday to Sunday");
        Assert(next.Tasks.All(task => task.Column == 7 && !task.IsCompleted), "all pending work arrives on the new Sunday's column");
        WeeklyPlanner.Initialize(state, new DateTime(2021, 1, 4));
        Equal("2021-01-04", state.ActiveWeekStart, "startup selects the new system week");
        Equal(3, state.Tasks.Count, "startup does not duplicate the carry");
    }

    private static void RolloverOrder()
    {
        var older = new WeekPlan
        {
            WeekStart = "2026-10-05",
            Tasks = new List<TaskItem>
            {
                new TaskItem { Id = "old-fri", Text = "Same text", Column = 5 },
                new TaskItem { Id = "old-mon-a", Text = "Same text", Column = 1 },
                new TaskItem { Id = "old-mon-b", Text = "Same text", Column = 1 },
                new TaskItem { Id = "old-done", IsCompleted = true, Column = 4 }
            }
        };
        var previous = new WeekPlan
        {
            WeekStart = "2026-10-12",
            Tasks = new List<TaskItem>
            {
                new TaskItem { Id = "new-tue", Column = 2 },
                new TaskItem { Id = "new-mon", Column = 1 }
            }
        };
        var current = new WeekPlan
        {
            WeekStart = "2026-10-19",
            Tasks = new List<TaskItem>
            {
                new TaskItem { Id = "existing", Column = 7 },
                new TaskItem { Id = "done", IsCompleted = true, Column = 7 },
                new TaskItem { Id = "other-day", Column = 3 }
            }
        };
        var state = new BoardState
        {
            ActiveWeekStart = previous.WeekStart,
            Tasks = previous.Tasks,
            Weeks = new List<WeekPlan> { current, previous, older }
        };
        Equal(5, WeeklyPlanner.RollOverUnfinished(state, new DateTime(2026, 10, 20)), "all actual expired occurrences move");
        Equal("done,existing,old-mon-a,old-mon-b,old-fri,new-mon,new-tue",
            string.Join(",", current.Tasks.Where(task => task.Column == 7).Select(task => task.Id)), "existing Sunday tasks precede stable chronological carryover");
        Equal("old-done", older.Tasks.Single().Id, "older completion retained");
        Equal(0, previous.Tasks.Count, "source loses pending tasks rather than copying them");
        Equal(0, WeeklyPlanner.RollOverUnfinished(state, new DateTime(2026, 10, 20)), "same-week repeat is a no-op");
        Equal(3, current.Tasks.Count(task => task.Text == "Same text"), "independent tasks with the same text are never deduplicated");
    }

    private static void RolloverDailyOccurrences()
    {
        var source = new TaskItem { Id = "daily-source", Text = "Daily sample", Color = "#123456", BackgroundColor = "#E0E8FF", Column = 1 };
        var state = new BoardState { Tasks = new List<TaskItem> { source } };
        BoardOperations.ApplyToEveryDay(state, source);
        string group = source.DailyGroupId;
        state.Tasks.Single(task => task.Column == 2).IsCompleted = true;
        state.Tasks.Single(task => task.Column == 5).IsCompleted = true;
        state.Tasks.Single(task => task.Column == 4).BackgroundColor = "#FFEEDD";
        var pendingIds = new HashSet<string>(state.Tasks.Where(task => !task.IsCompleted).Select(task => task.Id));
        WeeklyPlanner.Initialize(state, new DateTime(2026, 10, 15));
        Equal(5, WeeklyPlanner.RollOverUnfinished(state, new DateTime(2026, 10, 19)), "each missed daily occurrence is retained");
        Equal(2, state.Tasks.Count, "completed daily history remains in the source week");
        Assert(state.Tasks.All(task => task.IsDaily && task.IsCompleted), "completed daily membership is unchanged");
        WeekPlan current = state.Weeks.Single(plan => plan.WeekStart == "2026-10-19");
        Equal(12, current.Tasks.Count, "normal seven-day schedule plus five existing missed occurrences");
        Equal(7, current.Tasks.Count(task => task.IsDaily && task.DailyGroupId == group), "next week is seeded before source members detach");
        Equal(7, current.Tasks.Where(task => task.IsDaily).Select(task => task.Column).Distinct().Count(), "new daily schedule has exactly one occurrence per day");
        Assert(current.Tasks.Where(task => pendingIds.Contains(task.Id)).All(task => !task.IsDaily && task.Column == 7), "carried daily occurrences become independent Sunday work");
        Assert(current.Tasks.Where(task => !pendingIds.Contains(task.Id)).All(task => !task.IsCompleted), "normal daily occurrences start incomplete");
        Equal("#FFEEDD", current.Tasks.Where(task => task.IsDaily).Single(task => task.Column == 4).BackgroundColor, "normal next Thursday keeps its own background");
        Equal("#FFEEDD", current.Tasks.Single(task => pendingIds.Contains(task.Id) && task.BackgroundColor == "#FFEEDD").BackgroundColor, "carried occurrence retains its own background");
        WeeklyPlanner.SelectWeek(state, new DateTime(2026, 10, 19), new DateTime(2026, 10, 19));
        TaskItem nextRoutine = state.Tasks.First(task => task.IsDaily);
        BoardOperations.ApplyToEveryDay(state, nextRoutine);
        Equal(5, state.Tasks.Count(task => pendingIds.Contains(task.Id)), "reapplying the daily schedule cannot discard carried occurrences");
    }

    private static void RolloverNavigationIsolation()
    {
        DateTime sunday = new DateTime(2026, 10, 18);
        var state = Sample();
        WeeklyPlanner.Initialize(state, sunday);
        WeeklyPlanner.SelectWeek(state, sunday.AddDays(7), sunday);
        state.Tasks.Add(new TaskItem { Id = "future", Text = "Future sample", Column = 6 });
        WeeklyPlanner.SelectWeek(state, sunday.AddDays(14), sunday);
        Equal(0, state.Tasks.Count, "planning a later week does not move unfinished work");
        Equal(4, state.Weeks.Single(plan => plan.WeekStart == "2026-10-12").Tasks.Count, "source remains unchanged before actual rollover");
        Equal(0, WeeklyPlanner.RollOverUnfinished(state, sunday), "actual Sunday is not expired by a future view");
        Equal(4, WeeklyPlanner.RollOverUnfinished(state, sunday.AddDays(1)), "actual Monday moves the expired source");
        Equal("2026-10-26", state.ActiveWeekStart, "future view is retained by core rollover");
        WeekPlan next = state.Weeks.Single(plan => plan.WeekStart == "2026-10-19");
        Equal(6, next.Tasks.Single(task => task.Id == "future").Column, "existing future plan retains its assigned day");
        Equal(5, next.Tasks.Count, "only the newly current week receives carryover");
    }

    private static void RolloverSkippedWeeks()
    {
        var source = new TaskItem { Id = "old", Text = "Existing work", Column = 2 };
        var state = new BoardState { Tasks = new List<TaskItem> { source } };
        WeeklyPlanner.Initialize(state, new DateTime(2026, 9, 28));
        Equal(1, WeeklyPlanner.RollOverUnfinished(state, new DateTime(2026, 10, 19)), "missed weeks catch up directly to the current Sunday");
        Equal(2, state.Weeks.Count, "no intermediate week is synthesized");
        WeekPlan current = state.Weeks.Single(plan => plan.WeekStart == "2026-10-19");
        Assert(ReferenceEquals(source, current.Tasks.Single()), "the same occurrence moves across a long absence");
        Equal(0, WeeklyPlanner.RollOverUnfinished(state, new DateTime(2026, 9, 21)), "moving the clock backward cannot move future work backward");
        Equal(2, state.Weeks.Count, "backward clock does not create a redundant week");
        Equal(1, WeeklyPlanner.RollOverUnfinished(state, new DateTime(2026, 10, 26)), "still unfinished work moves again when its current week expires");
        Equal(0, current.Tasks.Count, "intermediate source is cleared on the next real rollover");
        Equal("old", state.Weeks.Single(plan => plan.WeekStart == "2026-10-26").Tasks.Single().Id, "identity survives repeated weekly moves");
    }

    private static void RolloverReplacedActiveList()
    {
        var state = Sample();
        DateTime previous = new DateTime(2026, 10, 12);
        WeeklyPlanner.Initialize(state, previous);
        state.Tasks = new List<TaskItem> { new TaskItem { Id = "after-undo", Text = "Replacement state", Column = 4 } };
        Equal(1, WeeklyPlanner.RollOverUnfinished(state, previous.AddDays(7)), "replacement list is authoritative");
        Equal(0, state.Tasks.Count, "replacement source list was moved");
        Assert(ReferenceEquals(state.Tasks, state.Weeks.Single(plan => plan.WeekStart == "2026-10-12").Tasks), "active plan remains attached to its replacement list");
        Equal("after-undo", state.Weeks.Single(plan => plan.WeekStart == "2026-10-19").Tasks.Single().Id, "stale archived tasks are not resurrected");
    }

    private static void RolloverPersistence()
    {
        InDirectory(delegate(string directory)
        {
            const string json = "{\"tasks\":[{\"id\":\"private-free-sample\",\"text\":\"Sample only\",\"color\":\"#123456\",\"backgroundColor\":\"#FFEEDD\",\"column\":2}],\"activeWeekStart\":\"2026-10-12\",\"weeks\":[],\"opaqueLegacy\":{\"empty\":\"\",\"value\":17}}";
            File.WriteAllText(Path.Combine(directory, "board.json"), json);
            var store = new BoardStore(directory);
            BoardState state = store.Load();
            WeeklyPlanner.Initialize(state, new DateTime(2026, 10, 19));
            Equal(7, state.Tasks.Single().Column, "startup automatically moves the saved pending task to Sunday");
            Equal("#FFEEDD", state.Tasks.Single().BackgroundColor, "background survives startup migration");
            store.Save(WeeklyPlanner.SnapshotForSave(state, null));
            string saved = File.ReadAllText(Path.Combine(directory, "board.json"));
            Assert(saved.Contains("\"opaqueLegacy\"") && saved.Contains("\"empty\":\"\"") && saved.Contains("\"value\":17"), "opaque legacy data survives rollover serialization");
            BoardState loaded = store.Load();
            WeeklyPlanner.Initialize(loaded, new DateTime(2026, 10, 19));
            Equal(1, loaded.Tasks.Count, "save and restart do not duplicate moved work");
            Equal("private-free-sample", loaded.Tasks.Single().Id, "saved carry retains identity");
            Equal(0, loaded.Weeks.Single(plan => plan.WeekStart == "2026-10-12").Tasks.Count, "saved source stays empty");
        });
    }

    private static void ManualNextWeekMove()
    {
        DateTime today = new DateTime(2026, 10, 15);
        var state = Sample();
        WeeklyPlanner.Initialize(state, today);
        TaskItem source = state.Tasks.Single(task => task.Id == "a");
        source.BackgroundColor = "#FFEEDD";
        source.IsCompleted = true;
        WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
        state.Tasks.Add(new TaskItem { Id = "existing", Text = "Next Sunday", Column = 7 });
        state.Tasks.Add(new TaskItem { Id = "done", IsCompleted = true, Column = 7 });
        WeeklyPlanner.SelectWeek(state, today, today);
        Assert(WeeklyPlanner.CanMoveToNextWeek(state), "ordinary date can move forward");
        Assert(WeeklyPlanner.MoveToNextWeek(state, source, today), "selected occurrence moves");
        Equal("2026-10-12", state.ActiveWeekStart, "manual move preserves source view");
        Equal(3, state.Tasks.Count, "only selected occurrence leaves source");
        WeekPlan target = state.Weeks.Single(plan => plan.WeekStart == "2026-10-19");
        Equal("done,a,existing", string.Join(",", target.Tasks.Select(task => task.Id)), "completion boundary is preserved and prior completed order is stable");
        Assert(ReferenceEquals(source, target.Tasks.Single(task => task.Id == "a")), "manual move retains the exact task instance");
        Equal("#A1B2C3", source.Color, "foreground preserved");
        Equal("#FFEEDD", source.BackgroundColor, "background preserved");
        Equal(7, source.Column, "manual destination is next Sunday");
        Assert(source.IsCompleted, "manual move preserves completion");
        Assert(!WeeklyPlanner.MoveToNextWeek(state, source, today), "moving an occurrence no longer in the source is a no-op");
    }

    private static void ManualDailyMove()
    {
        DateTime today = new DateTime(2026, 10, 15);
        var source = new TaskItem { Id = "daily", Text = "Daily sample", Column = 3, BackgroundColor = "#CCEEFF" };
        var state = new BoardState { Tasks = new List<TaskItem> { source } };
        BoardOperations.ApplyToEveryDay(state, source);
        string group = source.DailyGroupId;
        WeeklyPlanner.Initialize(state, today);
        Assert(WeeklyPlanner.MoveToNextWeek(state, source, today), "one selected daily occurrence moves");
        Equal(6, state.Tasks.Count(task => task.DailyGroupId == group), "other source daily occurrences are unaffected");
        WeekPlan next = state.Weeks.Single(plan => plan.WeekStart == "2026-10-19");
        Equal(8, next.Tasks.Count, "normal daily schedule was seeded before detaching the carried occurrence");
        Equal(7, next.Tasks.Count(task => task.DailyGroupId == group), "next week's daily schedule retains the source group");
        Assert(!source.IsDaily && next.Tasks.Last() == source, "manual daily carry is independent and last on Sunday");
        Equal(0, WeeklyPlanner.RollOverUnfinished(state, today), "manual future planning does not expire current week tasks");
        WeeklyPlanner.SelectWeek(state, today.AddDays(-21), today);
        var historical = new TaskItem { Id = "historical", Text = "Historical sample", Column = 2 };
        state.Tasks.Add(historical);
        Assert(WeeklyPlanner.MoveToNextWeek(state, historical, today), "manual move uses the viewed week even in history");
        WeekPlan historicalTarget = state.Weeks.Single(plan => plan.WeekStart == "2026-09-28");
        Equal("historical", historicalTarget.Tasks.Single().Id, "new historical target does not invent daily tasks");
        Equal("2026-09-21", state.ActiveWeekStart, "historical source view is retained");
    }

    private static void ManualMoveLimits()
    {
        var state = new BoardState();
        WeeklyPlanner.Initialize(state, new DateTime(9999, 12, 20));
        var source = new TaskItem { Id = "limit", Text = "Limit sample", Column = 1 };
        state.Tasks.Add(source);
        Assert(!WeeklyPlanner.CanMoveToNextWeek(state), "following Sunday's date must fit in DateTime");
        Assert(!WeeklyPlanner.MoveToNextWeek(state, source, DateTime.MaxValue), "out-of-range destination is rejected without mutation");
        Equal(1, source.Column, "rejected task keeps its original day");
        Equal(1, state.Weeks.Count, "rejected move does not create a week");
        WeeklyPlanner.SelectWeek(state, new DateTime(9999, 12, 13), DateTime.MaxValue);
        var valid = new TaskItem { Id = "valid", Column = 6 };
        state.Tasks.Add(valid);
        Assert(WeeklyPlanner.CanMoveToNextWeek(state), "last fully representable next Sunday is permitted");
        Assert(WeeklyPlanner.MoveToNextWeek(state, valid, DateTime.MaxValue), "valid edge move succeeds");
        Equal(7, valid.Column, "valid edge task arrives on Sunday");
        Equal("9999-12-13", state.ActiveWeekStart, "edge move retains source view");
        Assert(!WeeklyPlanner.MoveToNextWeek(state, null, DateTime.MaxValue), "null task is rejected");
        Assert(!WeeklyPlanner.MoveToNextWeek(state, new TaskItem { Id = "missing" }, DateTime.MaxValue), "foreign task is rejected");
        var minimum = new BoardState();
        WeeklyPlanner.Initialize(minimum, DateTime.MinValue);
        Equal(0, WeeklyPlanner.RollOverUnfinished(minimum, DateTime.MinValue), "minimum date rollover is a no-op");
        WeeklyPlanner.Initialize(state, DateTime.MaxValue);
        Equal("9999-12-27", state.ActiveWeekStart, "final partial system week remains representable");
        Equal(2, state.Tasks.Count, "final partial week can retain the existing pending work without constructing an invalid date");
        Assert(state.Tasks.All(task => task.Column == 7), "limit rollover uses the existing Sunday column safely");
    }

    private static void WeeklyMetadataValidation()
    {
        InDirectory(delegate(string directory)
        {
            var first = new TaskItem { Id = "first", Text = "First retained", Column = 1 };
            var second = new TaskItem { Id = "second", Text = "Second retained", Column = 2 };
            var state = new BoardState
            {
                ActiveWeekStart = "2026-10-13",
                Tasks = new List<TaskItem> { first },
                Weeks = new List<WeekPlan>
                {
                    new WeekPlan { WeekStart = "2026-10-13", Tasks = new List<TaskItem> { first } },
                    new WeekPlan { WeekStart = "2026-10-12", Tasks = new List<TaskItem> { second } }
                }
            };
            var store = new BoardStore(directory);
            store.Save(state);
            BoardState merged = store.Load();
            Equal("2026-10-12", merged.ActiveWeekStart, "active date canonicalized to Monday");
            Equal(1, merged.Weeks.Count, "duplicate dates merged");
            Equal("first,second", string.Join(",", merged.Tasks.Select(t => t.Id)), "unique tasks from duplicate active plans retained");
            Assert(ReferenceEquals(merged.Tasks, merged.Weeks[0].Tasks), "loaded active list attached to archive");
            store.Save(merged);
            string path = Path.Combine(directory, "board.json");
            string previous = File.ReadAllText(path);
            merged.Weeks.Add(new WeekPlan { WeekStart = "not-a-date" });
            bool failed = false;
            try { store.Save(merged); }
            catch (SerializationException) { failed = true; }
            Assert(failed, "invalid archive metadata rejects save");
            Equal(previous, File.ReadAllText(path), "failed validation leaves saved archive unchanged");
            const string invalid = "{\"tasks\":[],\"activeWeekStart\":\"2026-99-99\",\"weeks\":[]}";
            File.WriteAllText(path, invalid);
            BoardState recovered = store.Load();
            Equal(2, recovered.Tasks.Count, "metadata corruption recovers valid backup");
            string[] preserved = Directory.GetFiles(directory, "board.corrupt-*.json");
            Equal(1, preserved.Length, "invalid metadata original preserved");
            Equal(invalid, File.ReadAllText(preserved[0]), "invalid metadata bytes preserved");
        });
    }

    private static void ClearDayIsolation()
    {
        DateTime today = new DateTime(2026, 10, 15);
        BoardState state = Sample();
        TaskItem routine = state.Tasks[0];
        BoardOperations.ApplyToEveryDay(state, routine);
        routine.IsCompleted = true;
        state.Tasks.Add(new TaskItem { Id = "done-mon", Text = "Completed Monday", Column = 1, IsCompleted = true });
        WeeklyPlanner.Initialize(state, today);
        WeeklyPlanner.SelectWeek(state, today.AddDays(-7), today);
        state.Tasks.Add(new TaskItem { Id = "past-mon", Text = "Archived Monday", Column = 1, DailyGroupId = routine.DailyGroupId, IsCompleted = true });
        WeeklyPlanner.SelectWeek(state, today, today);
        WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
        List<TaskItem> future = state.Tasks;
        future[0].IsCompleted = true;
        WeeklyPlanner.SelectWeek(state, today, today);
        List<TaskItem> activeList = state.Tasks;
        List<TaskItem> otherDays = state.Tasks.Where(t => t.Column != 1).ToList();
        string otherOrder = string.Join(",", otherDays.Select(t => t.Id));
        Equal(3, BoardOperations.ClearDay(state, 1), "completed, ordinary and daily Monday items removed");
        Assert(!state.Tasks.Any(t => t.Column == 1), "selected active day is empty");
        Assert(ReferenceEquals(activeList, state.Tasks), "active list reference retained");
        Equal(otherOrder, string.Join(",", state.Tasks.Select(t => t.Id)), "other day order unchanged");
        Assert(otherDays.All(t => state.Tasks.Contains(t)), "other day objects retained");
        Equal(6, state.Tasks.Count(t => t.DailyGroupId == routine.DailyGroupId), "six sibling daily copies retained");
        WeekPlan past = state.Weeks.Single(w => w.WeekStart == "2026-10-05");
        Equal("Archived Monday", past.Tasks.Single().Text, "past Monday unchanged");
        Assert(past.Tasks.Single().IsCompleted, "past completion unchanged");
        Equal(7, future.Count, "future daily copies unchanged");
        Assert(future[0].IsCompleted, "future completion unchanged");
        Equal(0, BoardOperations.ClearDay(state, 1), "clearing an empty day is a no-op");
        Equal(otherOrder, string.Join(",", state.Tasks.Select(t => t.Id)), "empty clear does not reorder other days");
        state.Tasks = BoardOperations.CloneTasks(state.Tasks);
        Equal(1, BoardOperations.ClearDay(state, 2), "clear also handles an undo-replaced active list");
        BoardState saved = WeeklyPlanner.SnapshotForSave(state, null);
        Assert(!saved.Weeks.Single(w => w.WeekStart == state.ActiveWeekStart).Tasks.Any(t => t.Column == 1 || t.Column == 2), "weekly snapshot saves both clears after list replacement");
        Equal(1, saved.Weeks.Single(w => w.WeekStart == "2026-10-05").Tasks.Count, "snapshot retains past archive");
        Equal(7, saved.Weeks.Single(w => w.WeekStart == "2026-10-19").Tasks.Count, "snapshot retains future archive");
        foreach (int invalid in new[] { 0, 8 })
        {
            bool rejected = false;
            try { BoardOperations.ClearDay(state, invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(rejected, "out-of-range day rejected");
        }
        var empty = new BoardState { Tasks = null };
        Equal(0, BoardOperations.ClearDay(empty, 7), "uninitialized empty task list clears safely");
    }

    private static void LegacyUnknownFieldsRoundTrip()
    {
        InDirectory(delegate(string directory)
        {
            const string legacyJson = "{\"tasks\":[{\"id\":\"legacy\",\"text\":\"保留的任务\",\"color\":\"#123456\",\"column\":1}]," +
                "\"windowWidth\":1540.75,\"windowHeight\":0," +
                "\"recordFields\":[{\"id\":\"custom\",\"label\":\"睡眠\",\"values\":[\" 7.5 \",null,\"\",\"日\\n备注\",\"65.0\",\"1\",\"2\",\"extra retained\",\"   \"],\"futureUnit\":\"hours\"}]," +
                "\"legacyPreferences\":{\"nested\":[null,true,false,12.5,-3,{\"key\":\"value\"}],\"empty\":\"\",\"emptyObject\":{},\"emptyArray\":[],\"largeInteger\":9007199254740993,\"legacy key\":\"\",\"theme\":\"white\"}}";
            string path = Path.Combine(directory, "board.json");
            File.WriteAllText(path, legacyJson);
            var store = new BoardStore(directory);
            BoardState state = store.Load();
            Assert(state.ExtensionData != null, "unknown legacy fields captured");
            Equal("保留的任务", state.Tasks.Single().Text, "active task data still loads");
            DateTime today = new DateTime(2026, 10, 15);
            WeeklyPlanner.Initialize(state, today);
            WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
            state.Tasks.Add(new TaskItem { Id = "future", Text = "Future plan", Column = 2 });
            store.Save(WeeklyPlanner.SnapshotForSave(state, null));
            string[] opaqueNames = { "windowWidth", "windowHeight", "recordFields", "legacyPreferences" };
            foreach (string member in opaqueNames)
                Equal(JsonMember(legacyJson, member), JsonMember(File.ReadAllText(path), member), "opaque member retained: " + member);
            state = store.Load();
            WeeklyPlanner.Initialize(state, today);
            Equal("保留的任务", state.Tasks.Single().Text, "legacy task survives weekly migration");
            WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
            Equal("Future plan", state.Tasks.Single().Text, "new weekly data coexists with opaque legacy data");
            store.Save(WeeklyPlanner.SnapshotForSave(state, null));
            foreach (string member in opaqueNames)
                Equal(JsonMember(legacyJson, member), JsonMember(File.ReadAllText(path), member), "repeated roundtrip retains: " + member);
        });
    }

    private static void LegacyEmptyUnknownFields()
    {
        InDirectory(delegate(string directory)
        {
            string path = Path.Combine(directory, "board.json");
            var store = new BoardStore(directory);
            foreach (string value in new[] { "[]", "null" })
            {
                string legacyJson = "{\"tasks\":[],\"recordFields\":" + value + "}";
                File.WriteAllText(path, legacyJson);
                BoardState state = store.Load();
                WeeklyPlanner.Initialize(state, new DateTime(2026, 10, 15));
                store.Save(WeeklyPlanner.SnapshotForSave(state, null));
                Equal(JsonMember(legacyJson, "recordFields"), JsonMember(File.ReadAllText(path), "recordFields"), "explicit retired value retained: " + value);
            }
            store.Save(new BoardState());
            string freshJson = File.ReadAllText(path);
            Assert(JsonMember(freshJson, "recordFields") == null, "fresh board has no record defaults");
            Assert(JsonMember(freshJson, "windowWidth") == null, "fresh board has no saved width");
            Assert(JsonMember(freshJson, "windowHeight") == null, "fresh board has no saved height");
        });
    }

    private static string JsonMember(string json, string member)
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream,
            Encoding.UTF8, XmlDictionaryReaderQuotas.Max, null))
        {
            var document = new XmlDocument { PreserveWhitespace = true };
            document.Load(reader);
            XmlNode node = document.DocumentElement.SelectSingleNode(member);
            return node == null ? null : node.OuterXml;
        }
    }

    private static void BackgroundPersistence()
    {
        InDirectory(delegate(string directory)
        {
            const string json = "{\"tasks\":[" +
                "{\"id\":\"legacy\",\"text\":\"Synthetic legacy\",\"color\":\"#123456\",\"column\":1}," +
                "{\"id\":\"filled\",\"text\":\"Synthetic fill\",\"color\":\"#123456\",\"column\":2,\"backgroundColor\":\"#a0b1c2\"}," +
                "{\"id\":\"invalid\",\"text\":\"Invalid fill\",\"column\":3,\"backgroundColor\":\"#GG1234\"}," +
                "{\"id\":\"white\",\"text\":\"Explicit white\",\"column\":4,\"backgroundColor\":\"#ffffff\"}," +
                "{\"id\":\"alpha\",\"text\":\"Invalid alpha\",\"column\":5,\"backgroundColor\":\"#80112233\"}]}";
            string path = Path.Combine(directory, "board.json");
            File.WriteAllText(path, json);
            var store = new BoardStore(directory);
            BoardState state = store.Load();
            Assert(state.Tasks[0].BackgroundColor == null, "missing legacy fill is unfilled");
            Equal("#A0B1C2", state.Tasks[1].BackgroundColor, "valid fill normalized uppercase");
            Assert(state.Tasks[2].BackgroundColor == null, "malformed fill normalized unfilled");
            Equal("#FFFFFF", state.Tasks[3].BackgroundColor, "explicit white preserved separately from unfilled");
            Assert(state.Tasks[4].BackgroundColor == null, "unsupported alpha normalized unfilled");
            Assert(!TaskColors.IsValidHex(null) && !TaskColors.IsValidHex("") && !TaskColors.IsValidHex("#FFF"), "missing and short hex rejected");
            Assert(!TaskColors.IsValidHex("# 12345") && !TaskColors.IsValidHex(" #123456"), "hex whitespace rejected");
            Equal("#ABCDEF", TaskColors.NormalizeHex("#abcdef"), "shared color normalization");
            state.Tasks[1].BackgroundColor = "#a0b1c2";
            List<TaskItem> clones = BoardOperations.CloneTasks(state.Tasks);
            Equal("#a0b1c2", clones[1].BackgroundColor, "undo clone preserves fill");
            clones[1].BackgroundColor = "#FFFFFF";
            Equal("#a0b1c2", state.Tasks[1].BackgroundColor, "clone fill mutation independent");
            store.Save(state);
            Equal("#a0b1c2", state.Tasks[1].BackgroundColor, "save normalization leaves source fill untouched");
            Assert(JsonMember(File.ReadAllText(path), "tasks/item[1]/backgroundColor") == null, "unfilled background omitted from JSON");
            BoardState loaded = store.Load();
            Equal("#A0B1C2", loaded.Tasks[1].BackgroundColor, "filled task roundtrip");
            Assert(loaded.Tasks[0].BackgroundColor == null && loaded.Tasks[2].BackgroundColor == null, "unfilled tasks roundtrip");
        });
    }

    private static void BackgroundDailyIsolation()
    {
        BoardState state = Sample();
        TaskItem source = state.Tasks[0];
        source.BackgroundColor = "#F0E0D0";
        BoardOperations.ApplyToEveryDay(state, source);
        Assert(state.Tasks.Where(t => t.IsDaily).All(t => t.BackgroundColor == "#F0E0D0"), "new daily copies inherit source fill");
        TaskItem tuesday = state.Tasks.Single(t => t.IsDaily && t.Column == 2);
        TaskItem wednesday = state.Tasks.Single(t => t.IsDaily && t.Column == 3);
        tuesday.BackgroundColor = "#00AAAA";
        wednesday.BackgroundColor = null;
        source.BackgroundColor = "#FF0000";
        source.Text = "Synthetic routine edit";
        source.Color = "#ABCDEF";
        BoardOperations.SyncDailyTask(state, source);
        Equal("#00AAAA", tuesday.BackgroundColor, "daily sync retains Tuesday fill");
        Assert(wednesday.BackgroundColor == null, "daily sync retains explicit unfilled peer");
        Assert(state.Tasks.Where(t => t.IsDaily).All(t => t.Text == source.Text && t.Color == source.Color), "text and font color still sync");
        BoardOperations.ApplyToEveryDay(state, source);
        Equal("#00AAAA", tuesday.BackgroundColor, "reapply preserves existing independent fill");
        Assert(wednesday.BackgroundColor == null, "reapply preserves existing unfilled peer");
        state.Tasks.Remove(state.Tasks.Single(t => t.IsDaily && t.Column == 5));
        BoardOperations.ApplyToEveryDay(state, tuesday);
        Equal("#00AAAA", state.Tasks.Single(t => t.IsDaily && t.Column == 5).BackgroundColor, "recreated copy inherits selected source fill");
        Equal("#F0E0D0", state.Tasks.Single(t => t.IsDaily && t.Column == 4).BackgroundColor, "other existing copy remains independent");
        Equal("#FF0000", source.BackgroundColor, "original source retains its independent fill");
        Assert(state.Tasks.Where(t => !t.IsDaily).All(t => t.BackgroundColor == null), "ordinary task fills unaffected");
    }

    private static void BackgroundWeeklyCarry()
    {
        InDirectory(delegate(string directory)
        {
            DateTime today = new DateTime(2026, 10, 15);
            var state = new BoardState();
            var source = new TaskItem { Text = "Synthetic weekly routine", Color = "#123456", BackgroundColor = "#E0F0FF", Column = 1, IsCompleted = true };
            state.Tasks.Add(source);
            BoardOperations.ApplyToEveryDay(state, source);
            state.Tasks.Single(t => t.Column == 2).BackgroundColor = null;
            state.Tasks.Single(t => t.Column == 3).BackgroundColor = "#AAEECC";
            state.Tasks.Single(t => t.Column == 4).BackgroundColor = "#001122";
            state.Tasks.Remove(state.Tasks.Single(t => t.Column == 7));
            WeeklyPlanner.Initialize(state, today);
            List<TaskItem> history = state.Tasks;
            WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
            Equal(7, state.Tasks.Count, "new week contains seven routine copies");
            string[] expected = { "#E0F0FF", null, "#AAEECC", "#001122", "#E0F0FF", "#E0F0FF", "#E0F0FF" };
            for (int day = 1; day <= 7; day++)
                Equal(expected[day - 1], state.Tasks.Single(t => t.Column == day).BackgroundColor, "matching weekday or source fallback fill");
            Assert(state.Tasks.All(t => !t.IsCompleted), "fill carry does not carry completion");
            TaskItem futureWednesday = state.Tasks.Single(t => t.Column == 3);
            futureWednesday.BackgroundColor = "#ABCDEF";
            BoardOperations.SyncDailyTask(state, futureWednesday);
            Equal("#AAEECC", history.Single(t => t.Column == 3).BackgroundColor, "future fill edit does not change history");
            Assert(state.Tasks.Single(t => t.Column == 2).BackgroundColor == null, "future daily sync does not fill other days");
            List<TaskItem> committed = BoardOperations.CloneTasks(state.Tasks);
            state.Tasks.Single(t => t.Column == 2).BackgroundColor = "#112233";
            BoardState snapshot = WeeklyPlanner.SnapshotForSave(state, committed);
            Assert(snapshot.Tasks.Single(t => t.Column == 2).BackgroundColor == null, "draft fill excluded from save snapshot");
            Assert(snapshot.Weeks.Single(w => w.WeekStart == state.ActiveWeekStart).Tasks.Single(t => t.Column == 2).BackgroundColor == null, "draft fill excluded from active archive mirror");
            Equal("#AAEECC", snapshot.Weeks.Single(w => w.WeekStart == "2026-10-12").Tasks.Single(t => t.Column == 3).BackgroundColor, "history snapshot preserves fill");
            var store = new BoardStore(directory);
            store.Save(snapshot);
            BoardState loaded = store.Load();
            Equal("#ABCDEF", loaded.Tasks.Single(t => t.Column == 3).BackgroundColor, "active week fill saved");
            WeeklyPlanner.SelectWeek(loaded, today, today);
            Equal("#AAEECC", loaded.Tasks.Single(t => t.Column == 3).BackgroundColor, "archive fill saved independently");
            WeeklyPlanner.SelectWeek(loaded, today.AddDays(7), today);
            Assert(loaded.Tasks.Single(t => t.Column == 2).BackgroundColor == null, "unfilled weekday saved independently");
        });
    }

    private static void BackgroundLegacyMerge()
    {
        var first = new TaskItem { Id = "shared", Text = "Synthetic merge", Color = "#123456", Column = 1 };
        var second = new TaskItem { Id = "shared", Text = "Synthetic merge", Color = "#123456", Column = 1, BackgroundColor = "#AABBCC" };
        var state = new BoardState
        {
            Tasks = new List<TaskItem> { second },
            Weeks = new List<WeekPlan> { new WeekPlan { WeekStart = "2026-10-12", Tasks = new List<TaskItem> { first } } }
        };
        WeeklyPlanner.Initialize(state, new DateTime(2026, 10, 15));
        Equal(2, state.Tasks.Count, "background-only legacy difference retained");
        Equal(2, state.Tasks.Select(t => t.Id).Distinct().Count(), "merged differing task gets distinct ID");
        Assert(state.Tasks.Any(t => t.BackgroundColor == null) && state.Tasks.Any(t => t.BackgroundColor == "#AABBCC"), "both unfilled and filled variants preserved");
    }

    private static void FailedSave()
    {
        InDirectory(delegate(string directory)
        {
            var store = new BoardStore(directory);
            store.Save(Sample());
            string path = Path.Combine(directory, "board.json");
            string original = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try { store.Save(new BoardState()); }
                catch (IOException) { failed = true; }
                Assert(failed, "replacement must fail when the OS blocks replacement");
            }
            Equal(original, File.ReadAllText(path), "old file intact after failed replacement");
            Equal(0, Directory.GetFiles(directory, "*.tmp").Length, "temporary files cleaned");
            BoardState invalid = Sample();
            invalid.Tasks.Add(null);
            bool invalidFailed = false;
            try { store.Save(invalid); }
            catch (SerializationException) { invalidFailed = true; }
            Assert(invalidFailed, "invalid snapshot rejected");
            Equal(original, File.ReadAllText(path), "old file intact after invalid snapshot");
        });
    }

    private static void LockedOriginal()
    {
        InDirectory(delegate(string directory)
        {
            var store = new BoardStore(directory);
            store.Save(Sample());
            string path = Path.Combine(directory, "board.json");
            string original = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Equal(4, store.Load().Tasks.Count, "locked primary uses backup");
                bool failed = false;
                try { store.Save(new BoardState()); }
                catch (IOException) { failed = true; }
                Assert(failed, "unpreserved original blocks save");
            }
            Equal(original, File.ReadAllText(path), "locked original unchanged");
            Equal(4, store.Load().Tasks.Count, "reload after lock clears");
        });
    }

    private static void ExternalCorruption()
    {
        InDirectory(delegate(string directory)
        {
            var store = new BoardStore(directory);
            store.Save(Sample());
            File.WriteAllText(Path.Combine(directory, "board.json"), "externally damaged primary");
            store.Save(Sample());
            File.WriteAllText(Path.Combine(directory, "board.backup.json"), "externally damaged backup");
            store.Save(Sample());
            store.Save(Sample());
            string[] preserved = Directory.GetFiles(directory, "*.corrupt-*.json");
            Equal(2, preserved.Length, "external corrupt originals remain preserved");
            Assert(preserved.Any(p => File.ReadAllText(p) == "externally damaged primary"), "external primary preserved");
            Assert(preserved.Any(p => File.ReadAllText(p) == "externally damaged backup"), "external backup preserved");
            Equal(4, store.Load().Tasks.Count, "latest board still loads");
        });
    }

    private static void InboxPersistence()
    {
        InDirectory(delegate(string directory)
        {
            const string legacy = "{\"tasks\":[{\"id\":\"old\",\"text\":\"Synthetic old task\",\"column\":1}],\"legacyPreferences\":{\"label\":\"\",\"values\":[]}}";
            string path = Path.Combine(directory, "board.json");
            File.WriteAllText(path, legacy);
            var store = new BoardStore(directory);
            BoardState state = store.Load();
            Equal(0, state.Inbox.Count, "older files receive an empty inbox");
            DateTime today = new DateTime(2026, 10, 15);
            WeeklyPlanner.Initialize(state, today);
            for (int group = 1; group <= 3; group++)
            {
                InboxItem item = InboxOperations.Add(state, group);
                item.Task.Text = "Synthetic inbox 收集\n" + group;
                item.Task.Color = "#a1b2c3";
                item.Task.BackgroundColor = group == 2 ? "#ffffff" : null;
                item.Task.IsCompleted = group == 3;
            }
            List<InboxItem> expected = InboxOperations.CloneItems(state.Inbox);
            store.Save(WeeklyPlanner.SnapshotForSave(state, null));
            Equal(JsonMember(legacy, "legacyPreferences"), JsonMember(File.ReadAllText(path), "legacyPreferences"), "unknown root remains opaque beside inbox");
            state = store.Load();
            Equal(3, state.Inbox.Count, "every group persisted");
            for (int i = 0; i < 3; i++)
            {
                Equal(expected[i].Group, state.Inbox[i].Group, "group persisted");
                Equal(expected[i].Task.Id, state.Inbox[i].Task.Id, "identity persisted");
                Equal(expected[i].Task.Text, state.Inbox[i].Task.Text, "Unicode and line break persisted");
                Equal("#A1B2C3", state.Inbox[i].Task.Color, "font color normalized");
                Equal(expected[i].Task.IsCompleted, state.Inbox[i].Task.IsCompleted, "completion persisted");
                Equal(expected[i].Task.BackgroundColor == null ? null : "#FFFFFF", state.Inbox[i].Task.BackgroundColor, "background persisted");
            }
            WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
            Equal(3, state.Inbox.Count, "global inbox survives week switch");
            store.Save(WeeklyPlanner.SnapshotForSave(state, null));
            Equal(3, store.Load().Inbox.Count, "repeated roundtrip does not duplicate or lose inbox");
        });
    }

    private static void InboxNormalization()
    {
        InDirectory(delegate(string directory)
        {
            var state = new BoardState();
            var first = new InboxItem { Group = -4, Task = new TaskItem { Id = "duplicate", Text = "Synthetic first", Column = 7, Color = "invalid", BackgroundColor = "#aabbcc", DailyGroupId = "old-daily", IsCompleted = true } };
            state.Inbox.Add(first);
            state.Inbox.Add(new InboxItem { Group = 3, Task = new TaskItem { Id = "duplicate", Text = "Synthetic second" } });
            state.Inbox.Add(new InboxItem { Group = 2, Task = new TaskItem { Text = "  \n" } });
            var store = new BoardStore(directory);
            store.Save(state);
            BoardState loaded = store.Load();
            Equal(2, loaded.Inbox.Count, "blank editor draft omitted");
            Equal(1, loaded.Inbox[0].Group, "invalid group recovers into first group");
            Equal(3, loaded.Inbox[1].Group, "valid group preserved");
            Equal(2, loaded.Inbox.Select(item => item.Task.Id).Distinct().Count(), "duplicate inbox identities repaired");
            Equal("#111111", loaded.Inbox[0].Task.Color, "invalid font color recovers");
            Equal("#AABBCC", loaded.Inbox[0].Task.BackgroundColor, "background normalized");
            Assert(loaded.Inbox[0].Task.DailyGroupId == null && loaded.Inbox[0].Task.Column == 1, "inbox holds independent unscheduled work");
            Assert(loaded.Inbox[0].Task.IsCompleted, "completion retained");
            Assert(first.Group == -4 && first.Task.Column == 7 && first.Task.DailyGroupId == "old-daily", "normalization does not mutate live source");
            Equal(3, state.Inbox.Count, "save leaves draft editing state intact");
            string previous = File.ReadAllText(Path.Combine(directory, "board.json"));
            state.Inbox.Add(new InboxItem { Task = null });
            bool failed = false;
            try { store.Save(state); }
            catch (SerializationException) { failed = true; }
            Assert(failed, "null inbox task blocks destructive replacement");
            Equal(previous, File.ReadAllText(Path.Combine(directory, "board.json")), "invalid inbox save preserves previous data");
        });
    }

    private static void InboxSnapshotIsolation()
    {
        var state = new BoardState();
        DateTime today = new DateTime(2026, 10, 15);
        WeeklyPlanner.Initialize(state, today);
        InboxItem first = InboxOperations.Add(state, 1);
        first.Task.Text = "Synthetic saved inbox";
        first.Task.BackgroundColor = "#ABCDEF";
        BoardState snapshot = WeeklyPlanner.SnapshotForSave(state, null);
        Assert(!ReferenceEquals(snapshot.Inbox, state.Inbox) && !ReferenceEquals(snapshot.Inbox[0], first) &&
            !ReferenceEquals(snapshot.Inbox[0].Task, first.Task), "save snapshots deeply clone inbox");
        first.Task.Text = "Synthetic later edit";
        first.Task.BackgroundColor = null;
        Equal("Synthetic saved inbox", snapshot.Inbox[0].Task.Text, "snapshot text independent");
        Equal("#ABCDEF", snapshot.Inbox[0].Task.BackgroundColor, "snapshot background independent");
        var draft = InboxOperations.Add(state, 2);
        var committedSave = WeeklyPlanner.SnapshotForSave(state, null, snapshot.Inbox);
        Equal(1, committedSave.Inbox.Count, "committed inbox source excludes a live draft");
        Equal("Synthetic saved inbox", committedSave.Inbox[0].Task.Text, "save uses committed inbox rather than pending edits");
        committedSave.Inbox[0].Task.Text = "Synthetic independent save";
        Equal("Synthetic saved inbox", snapshot.Inbox[0].Task.Text, "save copy does not alias the committed source");
        InboxOperations.Remove(state, draft);
        List<InboxItem> undo = InboxOperations.CloneItems(state.Inbox);
        Assert(InboxOperations.ChangeGroup(state, first, 3), "group changes apply");
        Equal(1, undo[0].Group, "undo group independent");
        Assert(!InboxOperations.ChangeGroup(state, first, 3), "same group is no-op");
        WeeklyPlanner.SelectWeek(state, today.AddDays(-7), today);
        WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
        Equal(1, state.Inbox.Count, "no inbox per-week copies");
        Assert(ReferenceEquals(first, state.Inbox[0]), "week browsing preserves selected inbox object");
        Assert(InboxOperations.Remove(state, first) && !InboxOperations.Remove(state, first), "remove affects one existing item");
        Equal(1, undo.Count, "remove leaves undo snapshot intact");
    }

    private static void InboxScheduleIsolation()
    {
        var state = new BoardState();
        DateTime today = new DateTime(2026, 10, 15);
        WeeklyPlanner.Initialize(state, today);
        state.Tasks.Add(new TaskItem { Id = "current", Text = "Synthetic current week", Column = 2 });
        List<TaskItem> currentTasks = state.Tasks;
        InboxItem item = InboxOperations.Add(state, 2);
        item.Task.Text = "Synthetic scheduled";
        item.Task.Color = "#123456";
        item.Task.BackgroundColor = "#AABBCC";
        string id = item.Task.Id;
        WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
        state.Tasks.Add(new TaskItem { Id = "done", Text = "Synthetic done", Column = 2, IsCompleted = true });
        state.Tasks.Add(new TaskItem { Id = "pending", Text = "Synthetic pending", Column = 2 });
        state.Tasks.Add(new TaskItem { Id = "other", Text = "Synthetic other day", Column = 3 });
        Assert(InboxOperations.Schedule(state, item, 2), "inbox scheduled into selected week");
        Equal(0, state.Inbox.Count, "scheduling moves rather than copies");
        Equal("done,pending," + id, string.Join(",", state.Tasks.Where(task => task.Column == 2).Select(task => task.Id)), "pending work appends after current visible tasks");
        TaskItem moved = state.Tasks.Single(task => task.Id == id);
        Assert(ReferenceEquals(item.Task, moved), "scheduled task keeps identity object");
        Equal("#123456", moved.Color, "font retained");
        Equal("#AABBCC", moved.BackgroundColor, "fill retained");
        Equal(1, currentTasks.Count, "other week unchanged");
        Equal("current", currentTasks[0].Id, "other week identity unchanged");
        WeeklyPlanner.SelectWeek(state, today, today);
        Equal(1, state.Tasks.Count, "current week excludes future inbox transfer");
        WeeklyPlanner.SelectWeek(state, today.AddDays(7), today);
        Assert(state.Tasks.Any(task => task.Id == id), "target archive retains scheduled task");
    }

    private static void InboxDailyRoundTrip()
    {
        var state = new BoardState();
        var source = new TaskItem { Text = "Synthetic routine", Column = 1, Color = "#123456", BackgroundColor = "#ABCDEF", IsCompleted = true };
        state.Tasks.Add(source);
        BoardOperations.ApplyToEveryDay(state, source);
        string groupId = source.DailyGroupId;
        string id = source.Id;
        Assert(InboxOperations.MoveFromBoard(state, source, 3), "daily occurrence moves to requested inbox group");
        Equal(6, state.Tasks.Count, "only selected occurrence removed");
        Assert(state.Tasks.All(task => task.DailyGroupId == groupId), "other daily occurrences remain linked");
        InboxItem inbox = state.Inbox.Single();
        Equal(3, inbox.Group, "destination inbox group");
        Assert(inbox.Task.IsCompleted && !inbox.Task.IsDaily, "completion retained and daily relation detached");
        Equal(id, inbox.Task.Id, "collection preserves noncolliding ID");
        Assert(InboxOperations.Schedule(state, inbox, 7), "completed collected item can be rescheduled");
        Equal(0, state.Inbox.Count, "roundtrip consumes inbox item");
        TaskItem restored = state.Tasks.First(task => task.Column == 7);
        Assert(ReferenceEquals(source, restored) && restored.IsCompleted, "completed roundtrip respects completed ordering");
        Equal("#ABCDEF", restored.BackgroundColor, "roundtrip retains fill");
        Assert(!restored.IsDaily && state.Tasks.Count(task => task.DailyGroupId == groupId) == 6, "roundtrip does not relink or change daily peers");
    }

    private static void InboxTransferValidation()
    {
        var state = new BoardState();
        InboxItem existing = InboxOperations.Add(state, 1);
        existing.Task.Id = "collision";
        existing.Task.Text = "Synthetic existing inbox";
        var boardTask = new TaskItem { Id = "collision", Text = "Synthetic board", Column = 4 };
        state.Tasks.Add(boardTask);
        Assert(InboxOperations.MoveFromBoard(state, boardTask, 2), "collection resolves inbox ID collision");
        Assert(boardTask.Id != "collision" && state.Inbox.Select(item => item.Task.Id).Distinct().Count() == 2, "collection creates unique destination identity");
        string collectedId = boardTask.Id;
        state.Tasks.Add(new TaskItem { Id = collectedId, Text = "Synthetic same ID in target", Column = 1 });
        InboxItem collected = state.Inbox.Single(item => ReferenceEquals(item.Task, boardTask));
        Assert(InboxOperations.Schedule(state, collected, 5), "schedule resolves board ID collision");
        Assert(boardTask.Id != collectedId && state.Tasks.Select(task => task.Id).Distinct().Count() == 2, "scheduled identity unique within active week");
        Assert(!InboxOperations.Schedule(state, collected, 5), "same inbox item cannot transfer twice");
        Assert(!InboxOperations.Schedule(state, existing, 0) && !InboxOperations.Schedule(state, existing, 8), "unrepresentable weekdays rejected");
        InboxItem draft = InboxOperations.Add(state, 3);
        Assert(!InboxOperations.Schedule(state, draft, 1), "blank draft cannot be scheduled");
        Assert(!InboxOperations.MoveFromBoard(state, new TaskItem { Text = "Synthetic absent" }, 1), "unowned board item rejected");
        bool invalidGroup = false;
        try { InboxOperations.Add(state, 4); }
        catch (ArgumentOutOfRangeException) { invalidGroup = true; }
        Assert(invalidGroup, "only three defined groups can be added");
        Equal(2, state.Inbox.Count, "failed transfers leave inbox untouched");
    }

    private static void InDirectory(Action<string> action)
    {
        string testRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        string directory = Path.GetFullPath(Path.Combine(testRoot, "test-data-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally
        {
            // Cleanup is restricted to the exact test directory created above.
            if (!directory.StartsWith(Path.Combine(testRoot, "test-data-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing cleanup outside the test directory.");
            Directory.Delete(directory, true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }

    private static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.000000001)
            throw new InvalidOperationException("Expected " + expected + ", got " + actual);
    }
}
