using System.Collections;
using NUnit.Framework;
using SwissPoliceSim.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace SwissPoliceSim.Tests.PlayMode
{
    /// <summary>Starts the game from nothing: the bootstrap must load the world, start a new game and reach the Playing phase with a living player, traffic and law data.</summary>
    public sealed class BootstrapSmokeTest
    {
        [UnityTest, Timeout(180000)]
        public IEnumerator NewGame_ReachesPlaying()
        {
            var go = new GameObject("Boot"); var b = go.AddComponent<GameBootstrap>();
            float t = 0; while (b.Phase == BootPhase.Loading && t < 60) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.AreEqual(BootPhase.Menu, b.Phase, "World data did not load: " + b.Error);
            b.StartNew(b.SelectedCorps, 3);
            t = 0; while (b.Phase == BootPhase.Starting && t < 120) { t += Time.unscaledDeltaTime; yield return null; }
            Assert.AreEqual(BootPhase.Playing, b.Phase, "Game did not start: " + b.Error);
            Assert.NotNull(GameSession.I); Assert.NotNull(GameSession.I.Player); Assert.NotNull(GameSession.I.Obv); Assert.Greater(GameSession.I.Obv.Entries.Count, 50);
            for (int i = 0; i < 300; i++) yield return null;
            Assert.IsFalse(float.IsNaN(GameSession.I.Player.WorldPosition.y));
            Object.Destroy(go);
        }
    }
}
