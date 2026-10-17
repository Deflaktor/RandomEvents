using RoR2;
using System.Collections.Generic;

namespace RandomEvents
{
    // The buff stacks an event gave, so that it removes only those and leaves stacks from items and other sources alone
    public class BuffTracker
    {
        private readonly List<(CharacterBody body, BuffDef buff)> added = new List<(CharacterBody body, BuffDef buff)>();

        public void Add(CharacterBody body, BuffDef buff)
        {
            body.AddBuff(buff);
            added.Add((body, buff));
        }

        public bool Has(CharacterBody body, BuffDef buff)
        {
            return added.Contains((body, buff));
        }

        public void Remove(CharacterBody body, BuffDef buff)
        {
            if (added.Remove((body, buff)) && body)
            {
                body.RemoveBuff(buff);
            }
        }

        public void RemoveAll()
        {
            foreach (var (body, buff) in added)
            {
                if (body)
                {
                    body.RemoveBuff(buff);
                }
            }
            added.Clear();
        }
    }
}
