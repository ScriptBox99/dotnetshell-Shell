﻿using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System;
using System.Runtime.CompilerServices;
using Dotnet.Shell.Logic.Console;

[assembly: InternalsVisibleTo("UnitTests")]

namespace Dotnet.Shell.Logic.Suggestions
{
    class Suggestions
    {
        private CmdSuggestions cmdSuggestionsEngine;
        private CSharpSuggestions cSharpSuggestionsEngine;
        private string textWhichGeneratedSuggestions = string.Empty;
        private int textPosWhichGeneratedSuggestions = -1;

        private List<Suggestion> currentSuggestionsList = new List<Suggestion>();
        private int currentlySelectedSuggestion = -1;
        private bool hasTabJustBeenPressedForGroupOfSuggestions = false;

        private bool NeedsNewSuggestions(ConsoleImproved prompt) =>
            textWhichGeneratedSuggestions != prompt.UserEnteredText.ToString() ||
            textPosWhichGeneratedSuggestions != prompt.UserEnteredTextPosition ||
            currentlySelectedSuggestion == -1 ||
            currentSuggestionsList.Count == 0;

        public Suggestions(Dotnet.Shell.API.Shell shell)
        {
            cmdSuggestionsEngine = new CmdSuggestions(shell);
            cSharpSuggestionsEngine = new CSharpSuggestions();
        }

        public async Task OnTabSuggestCmdAsync(ConsoleImproved prompt, ConsoleKeyEx key)
        {
            await performCompletionAsync(prompt, key);
        }

        private async Task performCompletionAsync(ConsoleImproved prompt, ConsoleKeyEx key, int depth = 0)
        {
            if (NeedsNewSuggestions(prompt))
            {
                textWhichGeneratedSuggestions = prompt.UserEnteredText.ToString();
                textPosWhichGeneratedSuggestions = prompt.UserEnteredTextPosition;

                // invalidate state
                hasTabJustBeenPressedForGroupOfSuggestions = false;
                currentlySelectedSuggestion = -1;

                // try to get some suggestions, in the future i'd like this to be an API people can register to they can
                // write their own completion routines
                var newCmdSuggestions = await cmdSuggestionsEngine.GetSuggestionsAsync(textWhichGeneratedSuggestions, prompt.UserEnteredTextPosition);
                var newCSharpSuggestions = await cSharpSuggestionsEngine.GetSuggestionsAsync(textWhichGeneratedSuggestions, prompt.UserEnteredTextPosition);

                var newSuggestions = newCmdSuggestions.Union(newCSharpSuggestions).ToList();

                if (newSuggestions.Count != 0)
                {
                    currentSuggestionsList = newSuggestions;
                    currentlySelectedSuggestion = 0;

                    // We might have a common prefix to all our results, in which case we can fill that in for
                    // the user so they have less to type. 
                    var autoCompleteResults = currentSuggestionsList.Select(x => x.CompletionText);

                    var commonPrefix = new string(autoCompleteResults.First().Substring(0, autoCompleteResults.Min(s => s.Length))
                        .TakeWhile((c, i) => autoCompleteResults.All(s => s[i] == c)).ToArray());

                    // If we have autocompleted something we need to call this function again to update our state
                    // like if the user had typed the text in themselves
                    // But we only want to do this once as otherwise we could just keep on auto completing.
                    if (!string.IsNullOrWhiteSpace(commonPrefix) && depth == 0)
                    {
                        var commonIndex = currentSuggestionsList.First(x => x.CompletionText.StartsWith(commonPrefix)).Index;
                        prompt.ReplaceUserEntryAtPosition(commonPrefix, commonIndex);

                        // because we've changed the onscreen text we need to requery to get a better list of suggestions
                        await performCompletionAsync(prompt, key, depth + 1);
                        return;
                    }
                }
                else
                {
                    prompt.IgnoreTab();
                }
            }
            else
            {
                // increment the current index we are rendering
                currentlySelectedSuggestion = (currentlySelectedSuggestion + 1) % currentSuggestionsList.Count;
            }

            // now to render what we have, but first check if we've got a list in which case a double tab combo will render it
            // ignore all this if we our just refreshing our state
            if (depth == 0 && currentSuggestionsList.Count > 1)
            {
                if (hasTabJustBeenPressedForGroupOfSuggestions)
                {
                    await RenderAllSuggestionsAsync(prompt);
                }
                else
                {
                    prompt.IgnoreTab();
                    hasTabJustBeenPressedForGroupOfSuggestions = true;
                }
            }
            else if (depth == 0 && currentlySelectedSuggestion != -1)
            {
                RenderSuggestion(prompt, currentlySelectedSuggestion);
            }
        }

        private void RenderSuggestion(ConsoleImproved prompt, int suggestionIndex)
        {
            var text = currentSuggestionsList[suggestionIndex].CompletionText;
            var position = currentSuggestionsList[suggestionIndex].Index;

            prompt.ReplaceUserEntryAtPosition(text, position);
        }

        private async Task RenderAllSuggestionsAsync(ConsoleImproved prompt)
        {
            var cmd = prompt.UserEnteredText.ToString();
            var pos = prompt.CursorPosition;

            if (currentSuggestionsList.Count >= 100)
            {
                prompt.WriteLine();
                prompt.Write("Display all " + currentSuggestionsList.Count + " possibilities? (y or n) ");
                if (await prompt.GetCharAsync() == "Y")
                {
                    PerformRenderAll(prompt);
                }
            }
            else
            {
                PerformRenderAll(prompt);
            }

            prompt.DisplayPrompt();
            prompt.ReplaceUserEntryAtPosition(cmd, 0);
            prompt.CursorPosition = pos;
        }

        private void PerformRenderAll(ConsoleImproved prompt)
        {
            const int MinSpacing = 2;
            var width = prompt.Width;

            var longestStringLength = currentSuggestionsList.Max(x => x.FullText.Length + MinSpacing);
            var numberPerLine = (int)Math.Floor((double)width / (longestStringLength));

            var b = new StringBuilder();
            int writtenEntries = 0;

            prompt.WriteLine(b.ToString());

            foreach (var suggestion in currentSuggestionsList)
            {
                if (writtenEntries >= numberPerLine)
                {
                    writtenEntries = 0;
                    prompt.WriteLine(b.ToString());
                    b.Clear();
                }

                b.Append(suggestion.FullText + new string(' ', longestStringLength - suggestion.FullText.Length));
                writtenEntries++;
            }

            if (b.Length != 0)
            {
                prompt.WriteLine(b.ToString());
            }
            else
            {
                prompt.WriteLine();
            }
        }
    }
}
