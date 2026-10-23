using System.Collections.Generic;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// The points for a run: what each section of each world was worth and what the player scored in it. A
    /// section only counts toward the world and the game once it's finished (dropped out of, launched out of, or,
    /// for the last one, landed on the core from), both its points and what it was worth, so the world and game
    /// scores say how well the player has done so far.
    /// </summary>
    public class Scoreboard
    {
        public class WorldScore
        {
            public int World;
            public string Style;
            public int[] Earned;    // per section, including points not banked yet
            public int[] Possible;  // per section: every cube and checkpoint in it
            public bool[] Finished;

            public int Banked
            {
                get
                {
                    int sum = 0;
                    for (int i = 0; i < Earned.Length; i++) if (Finished[i]) sum += Earned[i];
                    return sum;
                }
            }

            /// <summary>What the finished sections were worth.</summary>
            public int PossibleSoFar
            {
                get
                {
                    int sum = 0;
                    for (int i = 0; i < Possible.Length; i++) if (Finished[i]) sum += Possible[i];
                    return sum;
                }
            }
        }

        public readonly List<WorldScore> Worlds = new List<WorldScore>();
        public WorldScore Current => Worlds.Count > 0 ? Worlds[Worlds.Count - 1] : null;

        public int GameBanked
        {
            get
            {
                int sum = 0;
                foreach (var w in Worlds) sum += w.Banked;
                return sum;
            }
        }

        public int GamePossible
        {
            get
            {
                int sum = 0;
                foreach (var w in Worlds) sum += w.PossibleSoFar;
                return sum;
            }
        }

        public void Reset() => Worlds.Clear();

        /// <summary>Starts scoring a world: each section is worth its cubes plus its checkpoints.</summary>
        public void StartWorld(int world, string style, MazeLayout layout, LevelSettings s)
        {
            int n = layout.Sections.Count;
            var w = new WorldScore { World = world, Style = style, Earned = new int[n], Possible = new int[n], Finished = new bool[n] };
            foreach (var c in layout.Cubes) w.Possible[c.Section] += c.Super ? s.superCubePoints : s.cubePoints;
            foreach (var c in layout.Checkpoints) w.Possible[c.Section] += s.checkpointPoints;
            Worlds.Add(w);
        }

        public void Add(int section, int points)
        {
            var w = Current;
            if (w == null || section < 0 || section >= w.Earned.Length || w.Finished[section]) return;
            w.Earned[section] += points;
        }

        /// <summary>Banks a section's points. False if it was already finished.</summary>
        public bool Finish(int section)
        {
            var w = Current;
            if (w == null || section < 0 || section >= w.Finished.Length || w.Finished[section]) return false;
            w.Finished[section] = true;
            return true;
        }

        /// <summary>"13 of 25 [52%]".</summary>
        public static string Format(int got, int of) =>
            $"{got} of {of} [{(of > 0 ? Mathf.FloorToInt(100f * got / of) : 0)}%]";
    }
}
