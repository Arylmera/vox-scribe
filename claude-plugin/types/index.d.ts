/** What the band shows; the module's timer writes it from the app's files, the drawing reads it. */
export type VoxBand = {
  kind: 'offline' | 'ready' | 'listening' | 'transcribing' | 'broken'
  /** This session holds the target: the command chord's dictation comes here. */
  armed: boolean
  /** When the current state began (app clock, ms): the listening timer counts from it. */
  since: number
  /** The app theme's accent, "#RRGGBB" (its dark variant). */
  accent: string
}

declare module 'claude-code' {
  interface PluginState {
    voxscribe: {
      band: VoxBand
      /** The clock while a dictation runs, for the waveform and the timer; 0 otherwise. */
      now: number
      /** The last outbox id taken: at most once, across a reload too. */
      lastHandled: string | null
    }
  }
}
