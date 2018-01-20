﻿using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEditor;

namespace Leap.Unity.Recording {
  using Query;

  public class TimelinePostProcess : MonoBehaviour {

    public TimelineAsset[] assets = new TimelineAsset[0];
    public string headPositionPath = "Leap Rig/Main Camera";
    public Vector3 startHeadPosition;
    public Vector3 endHeadPosition;

    public string[] allBindings;

    private void OnValidate() {
      allBindings = allClips.SelectMany(c => AnimationUtility.GetCurveBindings(c.Value)).
                             Select(b => b.path).
                             Distinct().
                             OrderBy(p => p).
                             ToArray();
    }

    [ContextMenu("Perform Post Process")]
    public void PerformPostProcess() {

      Dictionary<AnimationClip, TimeRange> ranges = new Dictionary<AnimationClip, TimeRange>();

      foreach (var pair in allClips) {
        var clip = pair.Key;
        var animClip = pair.Value;

        TimeRange range;
        if (!ranges.TryGetValue(animClip, out range)) {
          range = new TimeRange() {
            start = (float)clip.clipIn,
            end = (float)(clip.clipIn + clip.duration)
          };
        } else {
          range.start = Mathf.Min(range.start, (float)clip.clipIn);
          range.end = Mathf.Max(range.end, (float)(clip.clipIn + clip.duration));
        }

        ranges[animClip] = range;
      }

      foreach (var pair in allClips) {
        var clip = pair.Key;
        var animClip = pair.Value;

        CropAnimation(animClip, (float)clip.clipIn, (float)(clip.clipIn + clip.duration));
        BlendHeadPosition(clip, animClip);
      }

      AssetDatabase.Refresh();
      AssetDatabase.SaveAssets();
    }

    public void CropAnimation(AnimationClip clip, float start, float end) {
      var bindings = AnimationUtility.GetCurveBindings(clip);

      foreach (var binding in bindings) {
        var curve = AnimationUtility.GetEditorCurve(clip, binding);
        var cropped = AnimationCurveUtil.GetCropped(curve, start, end, slideToStart: false);
        AnimationUtility.SetEditorCurve(clip, binding, cropped);
      }
    }

    public void BlendHeadPosition(TimelineClip clip, AnimationClip animClip) {
      var bindings = AnimationUtility.GetCurveBindings(animClip);

      float startTime = (float)clip.clipIn;
      float endTime = (float)(clip.clipIn + clip.duration);

      var xBinding = bindings.Query().FirstOrNone(b => b.path == headPositionPath && b.propertyName == "m_LocalPosition.x");
      var yBinding = bindings.Query().FirstOrNone(b => b.path == headPositionPath && b.propertyName == "m_LocalPosition.y");
      var zBinding = bindings.Query().FirstOrNone(b => b.path == headPositionPath && b.propertyName == "m_LocalPosition.z");

      if (!xBinding.hasValue || !yBinding.hasValue || !zBinding.hasValue) {
        return;
      }

      xBinding.Match(xB => {
        yBinding.Match(yB => {
          zBinding.Match(zB => {
            bindings = new EditorCurveBinding[] { xB, yB, zB };
          });
        });
      });

      for (int i = 0; i < 3; i++) {
        var binding = bindings[i];
        var curve = AnimationUtility.GetEditorCurve(animClip, binding);

        float startPos = curve.Evaluate(startTime);
        float endPos = curve.Evaluate(endTime);

        float startOffset = startHeadPosition[i] - startPos;
        float endOffset = endHeadPosition[i] - endPos;

        var keys = curve.keys;
        for (int j = 0; j < keys.Length; j++) {
          var key = keys[j];

          float percent = Mathf.InverseLerp(startTime, endTime, key.time);
          float offset = Mathf.Lerp(startOffset, endOffset, percent);
          key.value += offset;

          curve.MoveKey(j, key);
        }

        AnimationUtility.SetEditorCurve(animClip, binding, curve);
      }
    }

    private IEnumerable<KeyValuePair<TimelineClip, AnimationClip>> allClips {
      get {
        foreach (var timeline in assets) {
          if (timeline == null) continue;

          foreach (var track in timeline.GetOutputTracks()) {
            var animTrack = track as AnimationTrack;
            if (animTrack != null) {
              foreach (var clip in animTrack.GetClips()) {
                var animClip = clip.underlyingAsset as AnimationClip;
                yield return new KeyValuePair<TimelineClip, AnimationClip>(clip, animClip);
              }
            }
          }
        }
      }
    }

    private struct TimeRange {
      public float start, end;
    }
  }
}