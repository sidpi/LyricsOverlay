import { AnimatePresence } from "framer-motion";
import { HeroStage } from "../components/HeroStage";
import { InspectorCard } from "../components/InspectorCard";
import { StyleGallery } from "../components/StyleGallery";
import { TopBar } from "../components/TopBar";
import { useAppStore } from "../store/useAppStore";

export function Dashboard() {
  const inspector = useAppStore((state) => state.inspector);

  return (
    <div className="relative h-full w-full overflow-hidden bg-[#050507]">
      <TopBar />

      <div className="scroll-area h-full overflow-y-auto overflow-x-hidden">
        <div className="pt-16">
          <HeroStage />
          <StyleGallery />
        </div>
      </div>

      <AnimatePresence>{inspector ? <InspectorCard key="inspector" /> : null}</AnimatePresence>
    </div>
  );
}
