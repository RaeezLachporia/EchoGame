using UnityEngine;
using UnityEngine.Animations; 

public class PlaySoundEnter : StateMachineBehaviour
{
    [SerializeField] private SoundType sound; 
    [SerializeField, Range(0,1)] private float volume = 1;
    [SerializeField] private Animator animator;
    [SerializeField] private Rigidbody body;


   
    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (body != null && body.linearVelocity.sqrMagnitude < 0.05f) return;
        AudioManager.PlaySound(sound, volume);
    }

}
