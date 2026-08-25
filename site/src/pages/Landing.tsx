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

// The landing page. The section order is the argument rather than a feature list: why it was
// rebuilt, then the picture (the part that decided the architecture), the pad, the screens,
// the evidence, the depth pages, the floor a machine without an NVIDIA card keeps, what it
// refuses to do, and the download.
//
// The page ends on the download, and nothing follows it: a reader who has read this far has
// decided, and the last thing they should meet is the button rather than another section.
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
      <FeatureIndex />
      <Floor />
      <NonGoals />
      <Download />
      <Footer />
    </>
  );
}
