import type { LyricFrame } from "../lib/clock";

export interface EngineColors {
  lyricText: string;
  highlight: string;
  secondaryText: string;
}

export interface EngineProps {
  frame: LyricFrame;
  colors: EngineColors;
  /** CSS font stack chosen in the Font inspector. */
  font: string;
  /** Live size of the stage in CSS pixels, for engines that lay out over the viewport. */
  stage?: { width: number; height: number };
}

export type EngineComponent = (props: EngineProps) => React.JSX.Element;
