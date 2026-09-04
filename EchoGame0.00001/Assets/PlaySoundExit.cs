using UnityEngine;

public class PlaySoundExit : StateMachineBehaviour
{
    [SerializeField] private SoundType sound;
    [SerializeField, Range(0, 1)] private float volume = 1;
    [Tooltip("If true, only play the sound when the animator's Rigidbody is actually moving. " +
             "Turn on for footsteps; leave off for landings and one-shot exits.")]
    [SerializeField] private bool requireMotion = false;
    [SerializeField] private float minSpeedSqr = 0.05f;


    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        Debug.Log($"[Enter] sound={sound} " +
                  $"layer={layerIndex} hash={stateInfo.shortNameHash} loop={stateInfo.loop}", animator);
        if (requireMotion )
        {
            
            Rigidbody rb = animator.GetComponent<Rigidbody>();
            if(rb == null) rb = animator.GetComponentInParent<Rigidbody>();
            if (rb != null && rb.linearVelocity.sqrMagnitude > minSpeedSqr) return;
        }
        AudioManager.PlaySound(sound, volume);
    }
}
