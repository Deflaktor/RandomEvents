using RoR2;
using System;
using System.Collections.Generic;

namespace RandomEvents
{
    // The events that change which monsters spawn share the stage's monster pool. It keeps the stage's own pool and
    // each event's change in order, and builds the pool from them again whenever an event ends, so the changes of the
    // events still running stay and the ended one's go. Rebuilding the pool from the stage instead would repeat the
    // chat message of a monster family.
    public static class MonsterPool
    {
        private class Change
        {
            public AbstractEvent user;
            public Func<WeightedSelection<DirectorCard>.ChoiceInfo, bool> keep;
            public WeightedSelection<DirectorCard> replacement;
        }

        private static ClassicStageInfo stageInfo;
        private static WeightedSelection<DirectorCard> originalSelection;
        private static WeightedSelection<DirectorCard> changedSelection;
        private static readonly List<Change> changes = new List<Change>();

        // the cards of the stage's monster pool, without the selection's spare capacity
        public static List<WeightedSelection<DirectorCard>.ChoiceInfo> GetChoices()
        {
            return GetChoices(ClassicStageInfo.instance != null ? ClassicStageInfo.instance.monsterSelection : null);
        }

        private static List<WeightedSelection<DirectorCard>.ChoiceInfo> GetChoices(WeightedSelection<DirectorCard> selection)
        {
            var choices = new List<WeightedSelection<DirectorCard>.ChoiceInfo>();
            if (selection != null)
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    choices.Add(selection.GetChoice(i));
                }
            }
            return choices;
        }

        // keeps only the cards that pass the filter, unless none do
        public static void Filter(AbstractEvent user, Func<WeightedSelection<DirectorCard>.ChoiceInfo, bool> keep)
        {
            Add(new Change { user = user, keep = keep });
        }

        // replaces the pool, e.g. with monsters the stage doesn't have; events that filter it afterwards filter these
        public static void Replace(AbstractEvent user, WeightedSelection<DirectorCard> selection)
        {
            if (selection != null && selection.Count > 0)
            {
                Add(new Change { user = user, replacement = selection });
            }
        }

        public static void Restore(AbstractEvent user)
        {
            if (changes.RemoveAll(change => change.user == user) == 0)
            {
                return;
            }
            var stage = ClassicStageInfo.instance;
            // unless the game rebuilt the pool since, which dropped the changes
            if (IsChanged(stage))
            {
                if (changes.Count > 0)
                {
                    Apply(stage);
                    return;
                }
                stage.monsterSelection = originalSelection;
            }
            if (changes.Count == 0)
            {
                stageInfo = null;
                originalSelection = null;
                changedSelection = null;
            }
        }

        private static bool IsChanged(ClassicStageInfo stage)
        {
            return stage != null && stage == stageInfo && stage.monsterSelection == changedSelection;
        }

        private static void Add(Change change)
        {
            var stage = ClassicStageInfo.instance;
            if (stage == null || stage.monsterSelection == null)
            {
                return;
            }
            // a new stage, or the game rebuilt the pool since, e.g. for an artifact
            if (!IsChanged(stage))
            {
                stageInfo = stage;
                originalSelection = stage.monsterSelection;
                changes.Clear();
            }
            changes.RemoveAll(other => other.user == change.user);
            changes.Add(change);
            Apply(stage);
        }

        private static void Apply(ClassicStageInfo stage)
        {
            var selection = originalSelection;
            foreach (var change in changes)
            {
                if (change.replacement != null)
                {
                    selection = change.replacement;
                    continue;
                }
                var filtered = new WeightedSelection<DirectorCard>();
                foreach (var choice in GetChoices(selection))
                {
                    if (change.keep(choice))
                    {
                        filtered.AddChoice(choice);
                    }
                }
                if (filtered.Count > 0)
                {
                    selection = filtered;
                }
            }
            changedSelection = selection;
            stage.monsterSelection = selection;
        }
    }
}
