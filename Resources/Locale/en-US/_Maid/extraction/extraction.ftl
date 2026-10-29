extraction-notify-popup = New evacuation message. Check the chat.
extraction-unlocked = Your objectives are done. Open your device, press "Mark extraction zone", then "Prepare pod" while inside the zone. Pod code: { $code }.
extraction-unlocked-forced = Evacuation was allowed early. Open your device, press "Mark extraction zone", then "Prepare pod" while inside the zone. Pod code: { $code }.
extraction-zone-designated = Extraction points designated:
    { $location }
    Stand inside any of the zones and press "Prepare pod" on your device. Re-requests left: { $left }.
extraction-no-requests-zone = The extraction points cannot be moved again. They are still here:
    { $location }
extraction-no-requests = No evacuation requests left.
extraction-no-zone = No suitable extraction point was found on the station.
extraction-zone-lost = All extraction points were destroyed. New ones are designated:
    { $location }
extraction-zone-lost-none = All extraction points were destroyed and no new ones could be found. Contact the admins.
extraction-zone-reset = You can request extraction points and call the pod again.
extraction-reminder = Evacuation is allowed. Open your device and press "Mark extraction zone". Pod code: { $code }.
extraction-reminder-zone = Extraction points:
    { $location }
    Pod code: { $code }.
extraction-not-in-zone = You must stand inside the extraction zone.
extraction-zone-coordinates = { $location }, coordinates: { $x }, { $y }
extraction-zone-marker-desc = Extraction marker of "{ $name }".
extraction-announcement-sender = Station sensors
extraction-round-end = [color=gold]{ $name }[/color]: evacuated { $minutes } { $minutes ->
    [one] minute
   *[other] minutes
} after arriving at the station.

extraction-pod-announcement = An unknown small object is moving towards the station at high speed, impact is imminent. Expected impact point: { $location }.
extraction-pod-called = The pod is called and lands in { $seconds } { $seconds ->
    [one] second
   *[other] seconds
} on the tile you are standing on. Everything on that tile will be destroyed, step aside!
extraction-pod-landed = The pod has landed. Open it with code { $code }, get inside, close it and press "START". In { $minutes } { $minutes ->
    [one] minute
   *[other] minutes
} the pod leaves the station on its own, with or without you.
extraction-pod-leaving-soon = The pod leaves the station in { $seconds } { $seconds ->
    [one] second
   *[other] seconds
}, with or without you!
extraction-pod-left-without = The pod left the station without you. There will be no other.
extraction-pod-lost = The pod could not land: its landing point is gone. The call is spent, there will be no other.
extraction-pod-destroyed = The pod was destroyed. There will be no other.
extraction-pod-inside = Press "START" to lift off.
extraction-pod-not-inside = You must be inside your pod.
extraction-pod-launching = The pod is sealed. Lift-off in { $seconds } { $seconds ->
    [one] second
   *[other] seconds
}.
extraction-pod-launch-cancelled = You left the pod, the launch is cancelled.
extraction-pod-sealed = The pod is sealed.
extraction-pod-ui-title = Drop pod
extraction-pod-ui-locked = Enter code
extraction-pod-ui-wrong-code = Wrong code
extraction-pod-ui-unlocked = Pod open
extraction-pod-ui-launching = Lifting off

command-description-extraction-reset = Removes the extraction zone of the given antagonists and lets them request it again.
command-description-extraction-unlock = Unlocks extraction for the given antagonists regardless of their objectives.

extraction-command-no-entity = Entity not found.
extraction-command-no-component = { $target } has no AntagExtractable component.
extraction-command-already-unlocked = { $target }: extraction is already unlocked.
extraction-command-unlocked = { $target }: extraction unlocked.
extraction-command-unlocked-added = { $target }: AntagExtractable added (everything carried now counts as own gear), extraction unlocked.
extraction-command-reset = { $target }: extraction zone reset.

extraction-device-unrecognized = We do not recognise you.
extraction-device-ui-title = Device of unknown purpose
extraction-device-ui-done = Early extraction objectives are done
extraction-device-ui-not-done = Early extraction objectives are not done
extraction-device-progress-percent = Done { $completed } of { $total } — { $percent }% (need { $needed }%)
extraction-device-progress-count = Done { $completed } of { $total } (need { $needed })
extraction-device-ui-objective = { $mark } { $title }
extraction-device-ui-objective-required = { $mark } { $title } (required)
extraction-device-ui-zone-status = Extraction zones: { $zone }
extraction-device-ui-pod-status = Pod: { $pod }.
extraction-device-ui-rerolls = Zone re-assignments left: { $count }
extraction-device-ui-code = Access code: { $code }.
extraction-device-ui-code-none = Access code: not received yet.
extraction-device-ui-refresh = Refresh
extraction-device-ui-zone = Mark extraction zone
extraction-device-ui-zone-reroll = Re-assign extraction zones
extraction-device-ui-zone-confirm = Really re-assign?
extraction-device-ui-pod = Prepare pod
extraction-device-zone-none = not marked
extraction-device-pod-none = not called
extraction-device-pod-flying = on its way, lands in { $seconds } s
extraction-device-pod-landed = on the station, leaves in { $time }
extraction-device-pod-landed-forever = on the station
extraction-device-pod-launching = lifting off in { $seconds } s
extraction-device-pod-gone = no longer available
extraction-device-ui-objectives-header = Objectives
