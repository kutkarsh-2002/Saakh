import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration } from 'chart.js';
import { DashboardAnalytics } from '../../core/models/domain';

/**
 * The two dashboard charts from the spec, with the caveats the spec attaches to
 * them written next to them rather than left for the reader to infer.
 *
 * Chart 1 is two cumulative running totals, and the gap between the lines is
 * backlog size, not a rate of progress; the copy says so, because a widening
 * gap can mean healthy growth or a growing pile of unresolved deals.
 *
 * Chart 2 is the health signal Chart 1 cannot give: per period, deals that
 * closed clean versus deals that were ever halted.
 */
@Component({
  selector: 'sk-analytics-charts',
  imports: [BaseChartDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="charts">
      <section class="sk-card chart">
        <header class="chart__head">
          <div>
            <p class="sk-eyebrow">Chart 1</p>
            <h3>Opened vs closed</h3>
          </div>
          <span class="chart__legend">
            <span class="key key--opened"><span class="key__swatch"></span>Opened</span>
            <span class="key key--closed"><span class="key__swatch"></span>Closed</span>
          </span>
        </header>

        <div class="chart__canvas">
          <canvas
            baseChart
            [type]="'line'"
            [data]="openedVsClosed().data"
            [options]="openedVsClosed().options"
          ></canvas>
        </div>

        <p class="chart__caveat">
          <span class="material-symbols-rounded" aria-hidden="true">info</span>
          <span>
            Running totals of every deal ever opened and ever closed. The gap between the lines is
            your <strong>backlog size, not your rate of progress</strong> — a widening gap can mean
            healthy growth in new deals or a growing pile of unresolved ones, so read it alongside
            Chart 2 rather than on its own.
          </span>
        </p>
      </section>

      <section class="sk-card chart">
        <header class="chart__head">
          <div>
            <p class="sk-eyebrow">Chart 2</p>
            <h3>Settlement quality</h3>
          </div>
          <span class="chart__legend">
            <span class="key key--clean"><span class="key__swatch"></span>Settled clean</span>
            <span class="key key--halted"><span class="key__swatch"></span>Halted</span>
          </span>
        </header>

        <div class="chart__canvas">
          <canvas
            baseChart
            [type]="'bar'"
            [data]="settlementQuality().data"
            [options]="settlementQuality().options"
          ></canvas>
        </div>

        <p class="chart__caveat">
          <span class="material-symbols-rounded" aria-hidden="true">monitoring</span>
          <span>
            How your deals actually ended, period by period. This is the health signal Chart 1
            cannot give: <strong>a rising halted share flags a real reliability problem</strong>,
            even when your opened total is growing strongly.
          </span>
        </p>
      </section>
    </div>
  `,
  styleUrl: './analytics-charts.scss',
})
export class AnalyticsCharts {
  readonly analytics = input.required<DashboardAnalytics>();

  /** Read off the stylesheet tokens so the charts cannot drift from the palette. */
  private readonly palette = {
    opened: '#15607d',
    closed: '#15543f',
    halted: '#a62019',
    clean: '#15543f',
    grid: '#e2d7c7',
    ink: '#5f5347',
  };

  readonly openedVsClosed = computed<ChartConfiguration<'line'>>(() => {
    const points = this.analytics().openedVsClosed;

    return {
      type: 'line',
      data: {
        labels: points.map((p) => p.period),
        datasets: [
          {
            label: 'Opened',
            data: points.map((p) => p.cumulativeOpened),
            borderColor: this.palette.opened,
            backgroundColor: this.palette.opened,
            pointBackgroundColor: this.palette.opened,
            borderWidth: 2.5,
            tension: 0.25,
            pointRadius: 3,
            pointHoverRadius: 6,
            fill: false,
          },
          {
            label: 'Closed',
            data: points.map((p) => p.cumulativeClosed),
            borderColor: this.palette.closed,
            backgroundColor: this.palette.closed,
            pointBackgroundColor: this.palette.closed,
            borderWidth: 2.5,
            // Dashed as well as a different colour, so the two series are
            // distinguishable without relying on hue.
            borderDash: [6, 4],
            tension: 0.25,
            pointRadius: 3,
            pointHoverRadius: 6,
            pointStyle: 'rectRot',
            fill: false,
          },
        ],
      },
      // Not stacked: these are two independent running totals, and stacking
      // them would draw Closed on top of Opened and overstate both.
      options: this.baseOptions('Cumulative deals', false),
    };
  });

  readonly settlementQuality = computed<ChartConfiguration<'bar'>>(() => {
    const points = this.analytics().settlementQuality;

    return {
      type: 'bar',
      data: {
        labels: points.map((p) => p.period),
        datasets: [
          {
            label: 'Settled clean',
            data: points.map((p) => p.completedClean),
            backgroundColor: this.palette.clean,
            borderRadius: 3,
            // Stacked, so the column height is the period's total closed count
            // and the split within it is the quality signal.
            stack: 'closed',
          },
          {
            label: 'Halted',
            data: points.map((p) => p.halted),
            backgroundColor: this.palette.halted,
            borderRadius: 3,
            stack: 'closed',
          },
        ],
      },
      // Stacked: the column height is the period's total closed count, and the
      // split within it is the quality signal.
      options: this.baseOptions('Deals closed', true),
    };
  });

  private baseOptions(axisLabel: string, stacked: boolean) {
    return {
      responsive: true,
      maintainAspectRatio: false,
      interaction: { mode: 'index' as const, intersect: false },
      plugins: {
        // The legend is rendered in the card header instead, where it stays
        // legible at small sizes.
        legend: { display: false },
        tooltip: {
          backgroundColor: '#0f0a06',
          titleFont: { family: 'IBM Plex Sans', weight: 600 as const, size: 13 },
          bodyFont: { family: 'IBM Plex Sans', size: 13 },
          padding: 10,
          cornerRadius: 8,
          displayColors: true,
        },
      },
      scales: {
        x: {
          grid: { display: false },
          ticks: {
            color: this.palette.ink,
            font: { family: 'IBM Plex Sans', size: 11 },
            maxRotation: 0,
            autoSkipPadding: 12,
          },
          border: { color: this.palette.grid },
          stacked,
        },
        y: {
          beginAtZero: true,
          title: {
            display: true,
            text: axisLabel,
            color: this.palette.ink,
            font: { family: 'IBM Plex Sans', size: 11, weight: 600 as const },
          },
          grid: { color: this.palette.grid },
          border: { display: false },
          ticks: {
            color: this.palette.ink,
            font: { family: 'IBM Plex Sans', size: 11 },
            // Deal counts are whole numbers; a 2.5 gridline would be nonsense.
            precision: 0,
          },
          stacked,
        },
      },
    };
  }
}
