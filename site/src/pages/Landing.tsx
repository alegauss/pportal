import { Nav } from "../components/Nav";
import { Footer } from "../components/Footer";
import { Hero } from "../components/sections/Hero";
import { Why } from "../components/sections/Why";
import { Picture } from "../components/sections/Picture";
import { Input } from "../components/sections/Input";
import { WindowSection } from "../components/sections/WindowSection";
import { Measured } from "../components/sections/Measured";
import { FeatureIndex } from "../components/sections/FeatureIndex";
import { Floor } from "../components/sections/Floor";
import { NonGoals } from "../components/sections/NonGoals";
import { Download } from "../components/sections/Download";
import { Ad } from "../components/ui/Ad";

// The landing page. The section order is the argument rather than a feature list: why it was
// rebuilt, then the picture (the part that decided the architecture), the pad, the screens,
// the evidence, the depth pages, the floor a machine without an NVIDIA card keeps, what it
// refuses to do, and the download.
//
// The page ends on the download, and nothing follows it: a reader who has read this far has
// decided, and the last thing they should meet is the button rather than another section.
// Which is also why this is the one page that turns the footer's ad slot off and places its
// own: the slot every other route ends with would land after that button here.
//
// The seam it takes instead is between the evidence and the depth pages. Every other boundary
// on this page is mid-argument - why into picture, picture into pad, floor into non-goals -
// and an ad dropped into one of those interrupts a sentence that is still being made. This
// one is where the argument has finished and the reader is choosing where to go next, so it
// is a pause that was already there rather than one the ad introduces.
export function Landing() {
  return (
    <>
      <Nav />
      <Hero />
      <Why />
      <Picture />
      <Input />
      <WindowSection />
      <Measured />
      <Ad slot="landing-mid" />
      <FeatureIndex />
      <Floor />
      <NonGoals />
      <Download />
      <Footer endSlot={false} />
    </>
  );
}
