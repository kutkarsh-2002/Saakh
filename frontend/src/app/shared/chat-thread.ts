import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Message } from '../core/models/domain';
import { formatDateTime, formatRelative } from '../core/util/format';

/**
 * The chat thread, used for pre-ticket negotiation and inside the deal
 * workspace. Messages are grouped by day and consecutive messages from the same
 * sender are joined, so a long negotiation stays readable on a small screen.
 */
@Component({
  selector: 'sk-chat-thread',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="thread" #scroller>
      @if (messages().length === 0) {
        <div class="thread__empty">
          <span class="material-symbols-rounded" aria-hidden="true">forum</span>
          <p class="thread__empty-title">{{ emptyTitle() }}</p>
          <p class="thread__empty-text">{{ emptyMessage() }}</p>
        </div>
      } @else {
        @for (group of grouped(); track group.day) {
          <p class="thread__day">
            <span>{{ group.day }}</span>
          </p>

          @for (bubble of group.messages; track bubble.id) {
            <div
              class="bubble"
              [class.bubble--mine]="bubble.mine"
              [class.bubble--joined]="bubble.joined"
            >
              @if (!bubble.joined) {
                <p class="bubble__who">{{ bubble.mine ? 'You' : bubble.senderName }}</p>
              }
              <p class="bubble__body">{{ bubble.body }}</p>
              <time class="bubble__time" [attr.datetime]="bubble.sentAt" [title]="bubble.full">
                {{ bubble.time }}
              </time>
            </div>
          }
        }
      }
    </div>

    @if (canSend()) {
      <form class="composer" (ngSubmit)="send()">
        <label for="chat-input" class="sk-sr-only">Type a message</label>
        <textarea
          id="chat-input"
          class="composer__input"
          rows="1"
          maxlength="4000"
          [(ngModel)]="draft"
          name="draft"
          [attr.placeholder]="placeholder()"
          (keydown.enter)="onEnter($event)"
        ></textarea>
        <button
          type="submit"
          class="sk-btn sk-btn--primary composer__send"
          [disabled]="!draft.trim() || sending()"
          aria-label="Send message"
        >
          <span class="material-symbols-rounded" aria-hidden="true">send</span>
        </button>
      </form>
      <p class="composer__hint">
        Enter sends. Shift and Enter together start a new line. Chat is kept as part of the deal's
        permanent record.
      </p>
    } @else {
      <p class="locked">
        <span class="material-symbols-rounded" aria-hidden="true">lock</span>
        {{ lockedMessage() }}
      </p>
    }
  `,
  styleUrl: './chat-thread.scss',
})
export class ChatThread implements AfterViewChecked {
  readonly messages = input.required<Message[]>();
  readonly canSend = input(true);
  readonly sending = input(false);
  readonly placeholder = input('Type a message');
  readonly lockedMessage = input('Chat opens once the interest is accepted.');
  readonly emptyTitle = input('No messages yet');
  readonly emptyMessage = input('Open with what you need and roughly when. Specifics get answers.');

  readonly sent = output<string>();

  private readonly scroller = viewChild<ElementRef<HTMLDivElement>>('scroller');
  private lastCount = -1;

  draft = '';

  readonly grouped = computed(() => {
    const groups: { day: string; messages: ChatBubble[] }[] = [];
    let previousSender: string | null = null;

    for (const message of this.messages()) {
      const day = dayLabel(message.sentAt);
      let group = groups.at(-1);

      if (!group || group.day !== day) {
        group = { day, messages: [] };
        groups.push(group);
        // A day break always starts a fresh attribution.
        previousSender = null;
      }

      group.messages.push({
        id: message.id,
        mine: message.mine,
        senderName: message.senderName,
        body: message.body,
        sentAt: message.sentAt,
        time: new Date(message.sentAt).toLocaleTimeString('en-IN', {
          hour: '2-digit',
          minute: '2-digit',
        }),
        full: formatDateTime(message.sentAt),
        joined: previousSender === message.senderProfileId,
      });

      previousSender = message.senderProfileId;
    }

    return groups;
  });

  ngAfterViewChecked(): void {
    // Jump to the newest message when the thread grows, but leave the scroll
    // position alone while the user is reading back through history.
    const count = this.messages().length;
    if (count !== this.lastCount) {
      this.lastCount = count;
      const element = this.scroller()?.nativeElement;
      if (element) {
        element.scrollTop = element.scrollHeight;
      }
    }
  }

  onEnter(event: Event): void {
    const keyboardEvent = event as KeyboardEvent;
    if (keyboardEvent.shiftKey) {
      return;
    }
    event.preventDefault();
    this.send();
  }

  send(): void {
    const body = this.draft.trim();
    if (!body) {
      return;
    }
    this.sent.emit(body);
    this.draft = '';
  }
}

interface ChatBubble {
  id: string;
  mine: boolean;
  senderName: string;
  body: string;
  sentAt: string;
  time: string;
  full: string;
  joined: boolean;
}

function dayLabel(value: string): string {
  const date = new Date(value);
  const today = new Date();
  const yesterday = new Date(today.getTime() - 86400000);

  const sameDay = (a: Date, b: Date) =>
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate();

  if (sameDay(date, today)) {
    return 'Today';
  }
  if (sameDay(date, yesterday)) {
    return 'Yesterday';
  }

  return date.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
}

export { formatRelative };
