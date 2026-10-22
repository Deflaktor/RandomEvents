using BepInEx.Bootstrap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RandomEvents
{
    // DebugToolkit replaces the console's autocompletion with its own, which suggests arguments only for the commands
    // registered with it. Its registration is internal, so it goes by reflection, and only when DebugToolkit is there.
    internal static class DebugToolkitAutoComplete
    {
        private const string Guid = "iHarbHD.DebugToolkit";

        internal static void Register(IEnumerable<AbstractEvent> events)
        {
            if (!Chainloader.PluginInfos.TryGetValue(Guid, out var plugin) || plugin.Instance == null)
            {
                return;
            }
            try
            {
                var assembly = plugin.Instance.GetType().Assembly;
                var parserType = assembly.GetType("DebugToolkit.AutoCompleteParser", true);
                var attributeType = assembly.GetType("DebugToolkit.AutoCompleteAttribute", true);
                var managerType = assembly.GetType("DebugToolkit.AutoCompleteManager", true);
                var parser = Activator.CreateInstance(parserType);
                // shown as "number|name", the name filled in
                parserType.GetMethod("RegisterStaticVariable", new[] { typeof(string), typeof(IEnumerable<string>), typeof(int) })
                    .Invoke(parser, new object[] { "random_event", events.Select(ev => $"{ev.Id}|{ev.GetEventConfigName()}").ToList(), 1 });
                var parse = attributeType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.NonPublic);
                var registerCommand = managerType.GetMethod("RegisterCommand", BindingFlags.Static | BindingFlags.NonPublic);
                void RegisterCommand(string command, string signature)
                {
                    var result = parse.Invoke(Activator.CreateInstance(attributeType, signature), new[] { parser });
                    registerCommand.Invoke(null, new[] { command, result });
                }
                RegisterCommand("start_event", "Requires 1 argument: {random_event}");
                RegisterCommand("queue_event", "Requires 1 argument: {random_event}");
                RegisterCommand("stop_event", "Requires 1 argument: {(random_event|'all')}");
            }
            catch (Exception e)
            {
                Log.LogWarning($"Could not add the event commands to DebugToolkit's autocompletion: {e}");
            }
        }
    }
}
