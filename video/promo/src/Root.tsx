import { Composition, Still } from 'remotion';
import { Promo } from './Promo';
import { Cover } from './Cover';

export const Root: React.FC = () => {
  return (
    <>
      <Composition
        id="Promo"
        component={Promo}
        durationInFrames={2220}
        fps={30}
        width={1920}
        height={1080}
      />
      <Still id="Cover" component={Cover} width={1920} height={1080} />
    </>
  );
};
