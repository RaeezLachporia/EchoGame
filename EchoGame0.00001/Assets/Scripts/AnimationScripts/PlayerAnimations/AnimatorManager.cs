using System;
using UnityEngine;

public class AnimatorManager : MonoBehaviour
{
   Animator animator;
   public int horizontal;
   public int vertical;
   int isSprinting;
   int isJumping;
   int isRolling;
   private void Awake()
   {
      animator = GetComponent<Animator>();
      horizontal = Animator.StringToHash("Horizontal");
      vertical = Animator.StringToHash("Vertical");
      isSprinting = Animator.StringToHash("isSprinting");
      isJumping = Animator.StringToHash("IsJumping");
      isRolling = Animator.StringToHash("IsRolling");
   }

   public void playJumpAnimation()
   {
      animator.SetTrigger(isJumping);
   }

   public void playRollAnimation()
   {
      animator.SetTrigger(isRolling);
   }

   public void updateAnimatorValues(float horizontalMovement, float verticalMovement, bool sprinting = false)
   {
      //Snapping animation
      float snappedHorizontal;
      float snappedVertical;
      
      #region Snapped Horizontal
      if (horizontalMovement > 0 && horizontalMovement < 0.55f)
      {
         snappedHorizontal = 0.5f;
      }
      else if (horizontalMovement > 0.55f)
      {
         snappedHorizontal = 1f;
      }
      else if(horizontalMovement < 0 && horizontalMovement > -0.55f)
      {
         snappedHorizontal = -0.5f;
      }
      else if (horizontalMovement < -0.55f)
      {
         snappedHorizontal = -1f;
      }
      else
      {
         snappedHorizontal = 0f;
      }
      #endregion
      #region Snapped Vertical
      if (verticalMovement > 1.55f)
      {
         snappedVertical = 2f;
      }
      else if (verticalMovement > 0.55f)
      {
         snappedVertical = 1f;
      }
      else if (verticalMovement > 0 && verticalMovement < 0.55f)
      {
         snappedVertical = 0.5f;
      }
      else if(verticalMovement < 0 && verticalMovement > -0.55f)
      {
         snappedVertical = -0.5f;
      }
      else if (verticalMovement < -0.55f)
      {
         snappedVertical = -1f;
      }
      else
      {
         snappedVertical = 0f;
      }
      #endregion
      animator.SetFloat(horizontal, snappedHorizontal, 0.1f, Time.deltaTime);
      animator.SetFloat(vertical, snappedVertical, 0.1f, Time.deltaTime);
      animator.SetBool(isSprinting, sprinting);
   }
}
