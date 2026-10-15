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
        Run("Task arrangement is stable and keeps normal tasks before daily tasks", DailyArrangement);
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
        const string expected = "normal-a,normal-b,daily-b,daily-a,tuesday-normal,tuesday-daily";
        Equal(expected, string.Join(",", state.Tasks.Select(t => t.Id)), "stable category order");
        BoardOperations.ArrangeTasks(state);
        Equal(expected, string.Join(",", state.Tasks.Select(t => t.Id)), "arrangement idempotent");
        state.Tasks.Add(new TaskItem { Id = "new-normal", Column = 1 });
        BoardOperations.ArrangeTasks(state);
        Equal("normal-a,normal-b,new-normal,daily-b,daily-a", string.Join(",", state.Tasks.Where(t => t.Column == 1).Select(t => t.Id)), "new normal task remains above routines");
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
        const string expected = "completed-normal-a,completed-daily,completed-normal-b,pending-normal-a,pending-normal-b,pending-daily-a,pending-daily-b,tuesday-completed,tuesday-pending";
        Equal(expected, string.Join(",", state.Tasks.Select(t => t.Id)), "three stable categories per day");
        BoardOperations.ArrangeTasks(state);
        Equal(expected, string.Join(",", state.Tasks.Select(t => t.Id)), "three category arrangement idempotent");
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
