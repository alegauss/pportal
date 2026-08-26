import { useAds } from "../../lib/ads";

/** The four layouts japode-ads draws. The site places the one that suits a full column. */
type AdFormat = "in-content" | "footer" | "sidebar" | "strip";

/**
 * One house ad slot.
 *
 * The container is rendered server-side and stays empty: the loader attaches a shadow root to
 * it and draws inside that, so the site stylesheet cannot reach the banner and the banner
 * cannot leak into the page. The box it will occupy is reserved in index.css, at the height
 * the loader reserves, so nothing on the page moves when it arrives - and when the catalogue
 * cannot be read the loader collapses the slot itself, leaving no gap.
 */
/**
 * `band` is which of the two containers the caller is standing in. A slot between top-level
 * sections needs the page's own `.wrap` around it to line up with them; a slot already inside
 * a column has one, and adding a second would indent the banner by that container's padding
 * and leave it narrower than the text it sits between.
 */
export function Ad({
  format = "in-content",
  slot,
  band = true,
}: {
  readonly format?: AdFormat;
  readonly slot: string;
  readonly band?: boolean;
}) {
  useAds();

  const unit = (
    <div
          className="ad"
          data-japode-ads=""
          data-ad-format={format}
          data-ad-slot={slot}
          // The loader never touches localStorage, so the recency memory it would otherwise
          // keep is not a thing this site has to declare. The network's own default is "on",
          // which rotates the last four campaigns for half an hour; this trades that for
          // having nothing to say about storage at all.
          data-ad-memory="off"
          // No data-ad-exclude. Self-exclusion is the host page's job in this network - a
          // campaign carries no host of its own, so a product keeps itself off its own site
          // by naming its id here. PPortal has no campaign in the catalogue to name, so there
          // is nothing to exclude, and an id that matches nothing would read as if there were.
        />
      </div>
    </div>
  );
}
