# Ecowitt Web Dashboard – Graph Functionality Requirements

**Source:** observed behaviour of the Ecowitt web dashboard (shared view
`https://www.ecowitt.net/home/share?authorize=…`, station GW3000A), inspected 2026-10-25.
**Keywords:** "shall" = mandatory, "should" = recommended, "may" = optional (RFC 2119 sense).

---

## 1. Scope

This document specifies the history-graph section of the Ecowitt dashboard: the
charts showing each sensor's recorded values over a selectable time period, the
controls that drive them, the alternative table view, and data export. The
live-value cards above the graphs appear only where they link to the graphs.

## 2. Definitions

| Term | Meaning |
|---|---|
| Station | A gateway device (e.g. GW3000A) plus its attached sensors. |
| Sensor group | A logical block of measurements, e.g. *Outdoor*, *Indoor*, *Rainfall*, or a named channel sensor (*Pool (1)*). |
| Series | One measured quantity drawn as one line, e.g. *Temperature*, *Dew Point*. |
| Period | The time window being displayed (Daily, Weekly, …). |
| Bucket | The time resolution of the data points within a period. |

---

## 3. Graph inventory

**GR-INV-01** The system shall render one chart per sensor group / quantity
family, stacked vertically in a single scrollable column, each chart spanning
the full content width at a fixed height (≈300 px).

**GR-INV-02** Each chart shall display a title naming its sensor group.
Several charts may share a title (e.g. three charts titled *Outdoor*).

**GR-INV-03** The system shall provide at least the following charts when the
corresponding sensors exist:

| ID | Title | Series | Y-axes | Chart type |
|---|---|---|---|---|
| C1 | Outdoor | Temperature, Feels Like, Dew Point | 1 | Line |
| C2 | Outdoor | Humidity | 1 | Line |
| C3 | Outdoor | VPD | 1 | Line |
| C4 | Indoor | Temperature, Feels Like, Dew Point | 1 | Line |
| C5 | Indoor | Humidity | 1 | Line |
| C6 | Solar and UVI | Solar (left axis), UVI (right axis, min 0) | 2 | Line |
| C7 | Rainfall | Rain Rate, Hourly, 24 Hours, Daily | 1 | Line |
| C8 | Rainfall | Weekly, Monthly, Yearly | 1 | Line |
| C9 | Wind | Wind Speed, Wind Gust (area-filled) | 1 | Line + area |
| C10 | Wind | Wind Direction | 1 (categorical) | Scatter |
| C11 | Pressure | Relative, Absolute | 1 | Line |
| C12…Cn | *Channel sensor name* (e.g. Pool (1), Keller (4), Sauna) | Temperature; Humidity in a separate chart where available | 1 | Line |
| Cb | Battery | Battery voltage per sensor with numeric voltage (e.g. Temperature Sensor CH1) | 1 | Line |

**GR-INV-04** Charts for channel sensors shall be generated dynamically from the
sensors registered on the station and shall use the user-assigned sensor name
followed by the channel number in parentheses.

**GR-INV-05** A chart for a quantity the sensor does not report (e.g. humidity
on a temperature-only probe) shall be omitted.

**GR-INV-06** The wind-direction chart shall plot direction as discrete points
(scatter) against a categorical (compass-sector) Y-axis rather than as a
continuous line.

## 4. Axes and data rendering

**GR-AX-01** The X-axis shall be a time axis using category labels in the
format `YYYY-MM-DD HH:mm`, with tick labels thinned automatically to avoid
overlap (displayed as `HH:mm` for intra-day periods).

**GR-AX-02** The X-axis shall always span the full selected period, even when
data exists for only part of it (e.g. the current day is drawn from 00:00 to
23:59, with the line ending at the latest received value).

**GR-AX-03** The Y-axis shall auto-scale to the data range of the visible
series, unless a fixed minimum is defined for that axis (e.g. UVI ≥ 0).

**GR-AX-04** Series shall be drawn as straight-segment polylines (no smoothing,
no step interpolation).

**GR-AX-05** Each series shall have a distinct, fixed colour, and the same
colour shall be used for that series in the legend and the tooltip.

**GR-AX-06** Values shall be displayed in the units configured for the station
(e.g. ℃, %, kPa, W/m², mm, mm/hr, km/h, hPa, V).

## 5. Statistical annotations

**GR-ST-01** The primary series of each chart (normally the first series) shall
show a **maximum marker** – a pin-shaped label carrying the maximum value,
placed at the point where it occurs.

**GR-ST-02** The primary series shall show a **minimum marker** in the same
way, in a contrasting colour (maximum: orange/red; minimum: blue).

**GR-ST-03** The primary series shall show the **average** over the displayed
period as a horizontal dashed reference line.

**GR-ST-04** Exceptions: the Rain Rate series shall show the maximum marker
only (no minimum, no average); the cumulative rainfall chart (C8) shall show no
markers; for the wind chart the markers and average line shall apply to *Wind
Gust*.

**GR-ST-05** Markers and the average line shall be recomputed whenever the
period, date, or visible data changes.

## 6. Legend

**GR-LG-01** Each chart shall display a legend at the top listing all its series.

**GR-LG-02** Clicking a legend entry shall toggle visibility of the
corresponding series; any number of series may be hidden at once.

**GR-LG-03** Hiding a series shall rescale the Y-axis to the remaining visible
series.

## 7. Tooltip and cross-chart synchronisation

**GR-TT-01** Hovering over a chart shall display a vertical cursor line and an
axis-triggered tooltip.

**GR-TT-02** The tooltip shall show the timestamp of the nearest data point
(`YYYY-MM-DD HH:mm`) followed by one line per visible series, containing the
series colour swatch, series name, value and unit
(e.g. `Temperature: 0.2 ℃`).

**GR-TT-03** All charts on the page shall be linked: hovering a time on any
chart shall display the cursor line and tooltip at the same timestamp on every
other chart simultaneously.

**GR-TT-04** The charts shall not provide in-chart zoom, pan, brush selection,
or a chart toolbox; time navigation shall be done exclusively through the
period controls (§8).

## 8. Period selection and time navigation

**GR-PR-01** A control bar shall sit above the charts and stay visible
(sticky) while the user scrolls through them. It shall contain: view toggle
(§10), period selector, previous / next buttons, date picker, and Export button.

**GR-PR-02** The period selector shall offer the following options, with the
default being *Daily*:

| Period | Window | Date-picker granularity | Bucket (observed) |
|---|---|---|---|
| Daily | Calendar day 00:00–23:59 | Day | 5 min |
| 24 Hours | Rolling, now − 24 h → now | Day | 5 min |
| Meteorological Day | 09:00 of selected day → 09:00 next day | Day | 5 min |
| Weekly | Calendar week, Monday–Sunday | Week (range) | 30 min |
| 7 Days | Rolling, now − 7 d → now | Range | 30 min |
| Monthly | Calendar month | Month | 4 h |
| 30 Days | Rolling, now − 30 d → now | Range | 4 h |
| Yearly | Calendar year | Year | 1 day |
| Custom | User-defined start and end date and time | Date-time range with hh:mm:ss pickers and **OK** confirmation | Chosen by the system according to span |

**GR-PR-03** For aggregated periods (Weekly, 7 Days, Monthly, 30 Days, Yearly)
the temperature-type charts shall show additional series *‹Quantity› Low* and
*‹Quantity› High* holding the minimum and maximum within each bucket, in
addition to the bucket value.

**GR-PR-04** The **previous** button shall move the window back by one period
unit (day / week / month / year), and the **next** button forward by one
unit. The next button shall be shown as disabled when the window already
contains the present.

**GR-PR-05** The date picker shall display the current window in the format
matching the period (`Mon/DD/YYYY`, `Mon/DD/YYYY - Mon/DD/YYYY`, `Mon/YYYY`,
`YYYY`) and shall allow direct selection of another window.

**GR-PR-06** Changing the period or the date shall reload all charts and the
table view for the new window.

**GR-PR-07** When no data exists for the selected window, the chart area shall
display a centred "No Data" placeholder instead of empty charts.

## 9. Navigation from live-value cards

**GR-NV-01** Each live-value card on the dashboard shall have a "Graph" icon
(with a "Graph" tooltip on hover) that scrolls the page to the corresponding
chart (`#<sensor-key>` anchor).

**GR-NV-02** Cards may be reordered by drag-and-drop, hidden (eye icon), and
configured (gear icon). The order and visibility of the charts should follow
the card configuration.

## 10. Table view

**GR-TB-01** The control bar shall provide a toggle between **Graph view**
(bar-chart icon, default) and **Table view** (list icon). Only one view shall
be visible at a time.

**GR-TB-02** The table view shall present the same data, for the same period
and date selection, as the graph view.

**GR-TB-03** The table shall have a frozen first column *Time* and a two-level
header: level 1 = sensor group, level 2 = quantity with unit in parentheses
(e.g. `Temperature(℃)`).

**GR-TB-04** Rows shall be sorted newest-first, one row per data bucket.

**GR-TB-05** The table shall scroll horizontally for the measurement columns
while the *Time* column stays fixed.

## 11. Export

**GR-EX-01** The **Export** button shall generate a spreadsheet of the data for
the currently selected period and date window and download it to the user's
device immediately, without an intermediate dialog.

**GR-EX-02** The export file shall be in Microsoft Excel format (`.xlsx`) and
shall be named `<DeviceName>(<YYYYMMDDHHmm start>-<YYYYMMDDHHmm end>).xlsx`,
e.g. `GW3000A-WIFIEF7B(202610250000-202610251154).xlsx`.

**GR-EX-03** Export shall be available in the shared (read-only, unauthenticated)
view.

## 12. Data refresh and access

**GR-DA-01** Chart data shall be loaded from the server asynchronously, one
request per chart group (observed endpoint `POST /index/get_data`).

**GR-DA-02** For windows that include the present, the dashboard shall show the
time since the station last reported ("Reported N seconds ago") and should
extend the charts with new data as it arrives.

**GR-DA-03** The graph section shall be available both to the authenticated
owner and through a share link (`/home/share?authorize=<code>`) without login.
In the shared view, settings shall be read-only.

## 13. Presentation and localisation

**GR-UI-01** The graphs shall use a dark theme (dark background, light axis
labels, coloured series).

**GR-UI-02** The user interface, including series names and legend labels,
shall be localisable; at least English and Simplified Chinese shall be offered.

**GR-UI-03** The layout shall be responsive; charts shall resize to the
available width.

---

## Appendix A – Observed details (informative)

* The charts are rendered with Apache ECharts (canvas renderer) inside a Vue.js
  application; all charts belong to one ECharts connection group (`group1`),
  which provides the tooltip synchronisation of GR-TT-03.
* For a full calendar day, the X-axis has 301 categories (5-minute buckets plus
  timestamps at irregular intervals); Weekly ≈ 339 (30 min), Monthly ≈ 188 (4 h),
  Yearly ≈ 367 (1 day).
* Selecting *Weekly* on a Sunday initially selected the **following** week
  (Mon 26 Oct – Sun 1 Nov 2026), which showed "No Data"; one step back gave
  the current week. This looks like a defect and is not part of the requirements.
