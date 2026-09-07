---
name: travel-planning
description: Plans a trip interactively in the chat using one rendered A2UI surface with three tabs — collects destination, dates, travellers and trip style, presents three fabricated itinerary options with prices, then a review and confirmation, moving the user from tab to tab as they answer. Use when the user wants to plan, book, price or compare a trip, holiday, vacation, flight or hotel stay, or asks for help choosing between travel options. Demonstration only — all itineraries and prices are invented, nothing is booked.
license: Apache-2.0
compatibility: Requires the render_skill_surface tool and a browser client with the A2UI renderer.
metadata:
  author: ntg-agent
  version: "2.0"
---

# Travel planning

You plan a trip in **one** surface with three tabs. You render it once at the start, and
after that you render the *same* surface again to fill in the next tab and move the user to
it. The card in the chat updates in place — it does not stack up, and there is never more
than one trip planner on screen.

**This is a demonstration.** There is no travel API. You invent the itineraries, airlines,
hotels and prices yourself — they must be plausible and internally consistent, never real
bookings. Say so once, in the confirmation step, and never claim anything was actually
reserved.

## The surface

Always `skill: "travel-planning"`, `surface: "trip-planner"`. There is no other surface.

| Tab | Index | Holds | User submits |
|---|---|---|---|
| 1. Trip | `0` | destination, dates, travellers, style · **Find trips** | `trip_search_submit` |
| 2. Options | `1` | three options with prices, a picker · **Continue to review** | `trip_option_selected` |
| 3. Review | `2` | the summary and total · **Confirm plan** / **Change details** | `trip_booking_confirmed` / `trip_change_requested` |

All three tabs exist from the first render. The user can click ahead at any time; tabs 2 and
3 simply show placeholders until you have filled them.

You move the user by sending the tab index in `__tabs.wizard`. The tab moves only when that
index *changes* — sending the same index twice does nothing, which is deliberate: if the user
has clicked back to an earlier tab to re-read it, a repeat render will not yank them forward.

## The data model

The surface's data is four independent branches, and each render writes them one at a time:

| Branch | Holds | Written by |
|---|---|---|
| `__tabs` | `{ "wizard": 0\|1\|2 }` — which tab is showing | you, on every render |
| `trip` | tab 1's fields: `destination`, `departDate`, `returnDate`, `travellers`, `style` | you (pre-fill), then the user |
| `options` | tab 2's `heading`, `subheading`, `o1`/`o2`/`o3` and the user's `choice` | you |
| `review` | tab 3's `destination`, `dates`, `travellers`, `option`, `total` | you |

**Send only the branch you are filling in, plus `__tabs`.** A branch you send is replaced by
what you sent merged over the template's defaults, so re-sending a branch you have nothing new
to say about overwrites live answers with blanks.

One exception, and only one: when you fill `review`, send `options` again with exactly the
values you sent last time. Tab 2 stays on screen behind tab 3, and the user can click back to
it while you are talking.

Nesting matters. `{"trip": "Da Nang"}` is rejected — `trip` is an object, and a value that
changes a branch's shape (object to text, array to text) is refused rather than rendered.

## Step 1 — Render the planner

Render it as soon as the user wants to plan a trip, with tab 0 showing.

**Always carry over what they have already said.** The message that started this almost always
names a destination — "a trip to Da Lat" means you send `trip.destination: "Da Lat"`. Do the
same for any dates, traveller count or style they mentioned. Rendering an empty form after
someone has just told you where they want to go makes them type it twice, and is the single
most common way this skill is used badly. Leave a field out only when they genuinely have not
said anything about it.

```json
{
  "skill": "travel-planning",
  "surface": "trip-planner",
  "values": {
    "__tabs": { "wizard": 0 },
    "trip": {
      "destination": "Da Nang",
      "departDate": "2026-09-12",
      "returnDate": "2026-09-19",
      "travellers": 2,
      "style": ["balanced"]
    }
  }
}
```

Dates are `YYYY-MM-DD` strings. `travellers` is a number, 1–8. `style` is an **array** with
one of `"budget"`, `"balanced"`, `"comfort"` — the picker stores its selection as an array
even in single-choice mode.

Say one short line before the surface ("Let's set up your trip.") and nothing after it. Do
not restate the fields in prose — the surface already shows them.

## Step 2 — Fill the Options tab

When `trip_search_submit` arrives, read the answers (see "Reading answers" below), then invent
**three** genuinely different options — not three near-identical ones:

- a cheaper, less convenient option (budget airline, longer layover, simpler hotel)
- a middle option that is the obvious default
- a premium option (direct flight, better location, more included)

Prices must reflect the trip length, traveller count and the chosen style, and must increase
across the three. Use the user's own currency if they named one, otherwise USD.

The user is already looking at this tab. Clicking **Find trips** moves them straight to
"2. Options", where they are watching placeholder text while you work — so answer promptly and
keep the values short. Still send `__tabs` with every render: it keeps your idea of the step and
theirs in agreement, and it costs one line.

Render `trip-planner` again with the `options` branch and tab 1:

```json
{
  "skill": "travel-planning",
  "surface": "trip-planner",
  "values": {
    "__tabs": { "wizard": 1 },
    "options": {
      "heading": "Three options for Da Nang",
      "subheading": "12–19 Sep · 2 travellers · balanced",
      "o1": { "name": "Budget saver",   "detail": "1 stop · 3-star beachfront · room only",       "price": "$610" },
      "o2": { "name": "Balanced pick",  "detail": "Direct · 4-star riverside · breakfast",        "price": "$840" },
      "o3": { "name": "Comfort plus",   "detail": "Direct · 5-star beachfront · breakfast + spa", "price": "$1,290" }
    }
  }
}
```

Do not send `trip` — the user is standing in it. Do not send `choice`; that is theirs.

`detail` is one line — keep it under about 45 characters or it wraps awkwardly next to the
price. `price` is a preformatted string including the currency symbol.

## Step 3 — Fill the Review tab

When `trip_option_selected` arrives, the `choice` value is `["o1"]`, `["o2"]` or `["o3"]`.
Render `trip-planner` again with `review` filled from the option they picked, `options`
carried forward unchanged, and tab 2:

```json
{
  "skill": "travel-planning",
  "surface": "trip-planner",
  "values": {
    "__tabs": { "wizard": 2 },
    "options": {
      "heading": "Three options for Da Nang",
      "subheading": "12–19 Sep · 2 travellers · balanced",
      "o1": { "name": "Budget saver",   "detail": "1 stop · 3-star beachfront · room only",       "price": "$610" },
      "o2": { "name": "Balanced pick",  "detail": "Direct · 4-star riverside · breakfast",        "price": "$840" },
      "o3": { "name": "Comfort plus",   "detail": "Direct · 5-star beachfront · breakfast + spa", "price": "$1,290" }
    },
    "review": {
      "destination": "Da Nang, Vietnam",
      "dates": "12–19 Sep 2026 (7 nights)",
      "travellers": "2 adults",
      "option": "Balanced pick — direct, 4-star riverside",
      "total": "$840"
    }
  }
}
```

`review` is five plain strings you write for the user to read — not the raw field values.

Then:

- On `trip_booking_confirmed` — reply in text confirming the plan, restate the option name and
  total, and state plainly that this is a demonstration and no booking was made. Do not render
  again.
- On `trip_change_requested` — render `trip-planner` with `__tabs.wizard: 0` and the `trip`
  branch re-filled with everything the user already gave you, so they are back on tab 1 with
  their answers in front of them. Then run steps 2 and 3 again from the top.

## Reading answers

Every action arrives as a `log_a2ui_event` tool call whose context contains the values the
button named **and** a `formData` object holding the complete surface state.

Check in this order:

1. The named value (e.g. `destination`, `choice`).
2. `formData` under the bound path — `formData.trip.destination`, `formData.options.choice`.
3. `formData.__inputs.<componentId>` — the fallback the client always writes, keyed by
   component id: `trip-destination`, `trip-depart`, `trip-return`, `trip-travellers`,
   `trip-style`, `options-picker`.

Step 3 is not a last resort here, it is often where the truth is: the client mirrors every
input into `__inputs` on each keystroke regardless of binding, and — unlike the four branches
above — nothing you render ever overwrites it. It is the one place an answer from an earlier
tab is guaranteed to still be sitting on a later turn.

`formData.__inputs.wizard` holds where the user is: `{ "index": 1, "title": "2. Options" }`.
Use it to tell a submission from the tab the user has since wandered to, and never to argue
with them about which tab they are on.

Picker values are arrays. `style` and `choice` come back as `["balanced"]` and `["o2"]` — take
the first element.

## Gotchas

- **One render per reply.** Fill one tab, move the user to it, then stop and wait. Never
  render twice in a turn to "get ahead" — the user has not answered the first one yet.
- **Never a second surface.** `trip-planner` is the whole flow. Re-rendering it updates the
  card that is already there; you are editing the planner, not posting a new one.
- **Never invent a real booking reference, airline PNR, or payment confirmation.** Fabricate
  itineraries and prices; never fabricate anything that looks like proof of a transaction.
- **Don't narrate the UI.** Do not describe the tabs or buttons or tell the user to click
  something — they can see it. One short line of framing is enough.
- **If the user answers in text instead of using the surface**, just accept it and fill the
  next tab from what they said. Don't insist they use the form.
- **If the user asks for something outside a trip plan** (visa rules, weather, packing advice),
  answer normally in text. Only render when you are advancing one of the three tabs.
