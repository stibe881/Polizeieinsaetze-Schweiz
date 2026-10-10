using System.Collections;
using NUnit.Framework;
using SwissPoliceSim.Vehicle;
using SwissPoliceSim.World;
using UnityEngine;
using UnityEngine.TestTools;

namespace SwissPoliceSim.Tests
{
    /// <summary>
    /// End-to-end smoke test of Phase 1: bootstrap -> tiles stream in -> patrol car spawns on a real road, settles on
    /// the ground, drives forward under throttle and the origin/streaming keep working.
    /// NOTE: written but NOT yet executed (no Unity editor was available when this was authored).
    /// </summary>
    public class Phase1SmokeTest
    {
        [UnityTest]
        public IEnumerator Bootstrap_SpawnsCar_AndCarDrives()
        {
            var go = new GameObject("PilotBootstrap");
            go.AddComponent<PilotBootstrap>();

            PoliceVehicleController car = null;
            for (int i = 0; i < 60 * 30 && car == null; i++)   // up to ~30 s
            {
                yield return null;
                car = Object.FindFirstObjectByType<PoliceVehicleController>();
            }
            Assert.IsNotNull(car, "patrol car was not spawned (is SwissWorld data baked?)");

            yield return new WaitForSeconds(3f);                       // let suspension settle
            var lv0 = WorldOrigin.ToLv95(car.transform.position);
            Assert.That(lv0.H, Is.InRange(520, 600), "car should stand on Bern terrain (~540 m)");
            Assert.Less(car.SpeedKmh, 3f);

            car.Throttle = 1f;
            yield return new WaitForSeconds(4f);
            Assert.Greater(car.SpeedKmh, 15f, "car should accelerate");
            var lv1 = WorldOrigin.ToLv95(car.transform.position);
            double moved = System.Math.Sqrt(System.Math.Pow(lv1.E - lv0.E, 2) + System.Math.Pow(lv1.N - lv0.N, 2));
            Assert.Greater(moved, 10.0);
            Assert.Greater(Object.FindFirstObjectByType<TileStreamingManager>().LoadedTileCount, 9);
        }
    }
}
