using RoR2;
using UnityEngine;

namespace RandomEvents
{
    // Throws a character in an arc that lands at a point. The host can throw anyone this way, also another player,
    // whose game moves them.
    public static class Throw
    {
        // flying at about the speed, within the shortest and longest time in the air
        public static void ToPoint(HealthComponent victim, Vector3 target, float speed, float minTime, float maxTime)
        {
            Vector3 path = target - victim.body.footPosition;
            float time = Mathf.Clamp(path.magnitude / speed, minTime, maxTime);
            Vector3 velocity = path / time;
            // so that gravity bends the flight to end at the target
            velocity.y -= 0.5f * Gravity(victim) * time;
            var forceInfo = PhysForceInfo.Create();
            forceInfo.force = velocity;
            forceInfo.massIsOne = true;
            forceInfo.resetVelocity = true;
            forceInfo.ignoreGroundStick = true;
            forceInfo.disableAirControlUntilCollision = true;
            forceInfo.respectKnockupImmune = true;
            victim.TakeDamageForce(forceInfo);
        }

        // the downward pull on the victim, negative
        private static float Gravity(HealthComponent victim)
        {
            if (victim.TryGetComponent(out CharacterMotor characterMotor))
            {
                return characterMotor.useGravity && !characterMotor.isFlying ? Physics.gravity.y * characterMotor.gravityScale : 0f;
            }
            if (victim.TryGetComponent(out Rigidbody rigidbody))
            {
                return rigidbody.useGravity ? Physics.gravity.y : 0f;
            }
            return 0f;
        }
    }
}
