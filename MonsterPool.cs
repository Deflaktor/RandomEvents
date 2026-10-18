using RoR2;
using System;
using System.Collections.Generic;

namespace RandomEvents
{
    // The events that change which monsters spawn share the stage's monster pool: the first to filter it saves it,
    // and the last to end puts it back. Rebuilding the pool instead would repeat the chat message of a monster family.
    public static class MonsterPool
    {
        private static ClassicStageInfo stageInfo;
        private static WeightedSelection<DirectorCard> originalSelection;
        private static WeightedSelection<DirectorCard> filteredSelection;
        private static readonly HashSet<AbstractEvent> users = new HashSet<AbstractEvent>();

        // the cards of the stage's monster pool, without the selection's spare capacity
        public static List<WeightedSelection<DirectorCard>.ChoiceInfo> GetChoices()
        {
            var choices = new List<WeightedSelection<DirectorCard>.ChoiceInfo>();
            var selection = ClassicStageInfo.instance != null ? ClassicStageInfo.instance.monsterSelection : null;
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
            var stage = ClassicStageInfo.instance;
            if (stage == null || stage.monsterSelection == null)
            {
                return;
            }
            // a new stage, or the game rebuilt the pool since, e.g. for an artifact
            if (stage != stageInfo || stage.monsterSelection != filteredSelection)
            {
                stageInfo = stage;
                originalSelection = stage.monsterSelection;
                users.Clear();
            }
            var filtered = new WeightedSelection<DirectorCard>();
            foreach (var choice in GetChoices())
            {
                if (keep(choice))
                {
                    filtered.AddChoice(choice);
                }
            }
            if (filtered.Count == 0)
            {
                return;
            }
            users.Add(user);
            filteredSelection = filtered;
            stage.monsterSelection = filtered;
        }

        // replaces the pool, e.g. with monsters the stage doesn't have; events that filter it afterwards filter these
        public static void Replace(AbstractEvent user, WeightedSelection<DirectorCard> selection)
        {
            var stage = ClassicStageInfo.instance;
            if (stage == null || selection == null || selection.Count == 0)
            {
                return;
            }
            if (stage != stageInfo || stage.monsterSelection != filteredSelection)
            {
                stageInfo = stage;
                originalSelection = stage.monsterSelection;
                users.Clear();
            }
            users.Add(user);
            filteredSelection = selection;
            stage.monsterSelection = selection;
        }

        public static void Restore(AbstractEvent user)
        {
            if (!users.Remove(user) || users.Count > 0)
            {
                return;
            }
            // unless the game rebuilt the pool since
            if (stageInfo != null && stageInfo == ClassicStageInfo.instance && stageInfo.monsterSelection == filteredSelection)
            {
                stageInfo.monsterSelection = originalSelection;
            }
            stageInfo = null;
            originalSelection = null;
            filteredSelection = null;
        }
    }
}
