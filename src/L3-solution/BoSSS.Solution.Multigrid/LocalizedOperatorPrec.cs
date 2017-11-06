﻿/* =======================================================================
Copyright 2017 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using ilPSP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BoSSS.Foundation;
using BoSSS.Solution.NSECommon;
using BoSSS.Solution.Utils;
using ilPSP.LinSolvers;
using ilPSP;
using ilPSP.Connectors.Matlab;

namespace BoSSS.Solution.Multigrid
{
    public class LocalizedOperatorPrec : ISolverSmootherTemplate, ISolverWithCallback
    {
        public int IterationsInNested {
            get {
                return 0;
                //throw new NotImplementedException();
            }
        }

        public int ThisLevelIterations {
            get {
                return 0;
                //throw new NotImplementedException();
            }
        }

        public bool Converged {
            get { return this.m_Converged; }
        }

        public Action<int, double[], double[], MultigridOperator> IterationCallback {
            get {
                return null;
                //throw new NotImplementedException();
            }
            set {

                //throw new NotImplementedException();
            }
        }

        int D;
        string[] DomName;
        string[] CodName;
        string[] Params;

        string[] CodNameSelected = new string[0];
        string[] DomNameSelected = new string[0];

        public double m_dt = 1;
        public double m_muA;

        int[] Uidx, Pidx;

        SpatialOperator LocalOp;

        MsrMatrix LocalMatrix,ConvDiff, pGrad, divVel,P;

        public void Init(MultigridOperator op)
        {
            int D = op.GridData.SpatialDimension;

            CodName = (new string[] { "mom0", "mom1" });
            Params = ArrayTools.Cat(
                VariableNames.Velocity0Vector(D));
            DomName = ArrayTools.Cat(VariableNames.VelocityVector(D));

            LocalOp = new SpatialOperator(DomName, Params, CodName, (A, B, C) => 4);

            for (int d = 0; d < D; d++)
            {

                LocalOp.EquationComponents["mom" + d].Add(new LocalDiffusiveFlux() { m_component = d, dt = m_dt, muA = m_muA });

            }

            LocalOp.Commit();

            //LocalMatrix = op.MassMatrix.CloneAs().ToMsrMatrix();
            //LocalMatrix.Clear();

            UnsetteledCoordinateMapping test = new UnsetteledCoordinateMapping(op.BaseGridProblemMapping.BasisS.GetSubVector(0, D));

            var U0_U0mean = new SinglePhaseField[D];

            LocalMatrix = LocalOp.ComputeMatrix(test, U0_U0mean, test, time: m_dt);


            Uidx = op.Mapping.ProblemMapping.GetSubvectorIndices(true, D.ForLoop(i => i));
            Pidx = op.Mapping.ProblemMapping.GetSubvectorIndices(true, D);

            int Upart = Uidx.Length;
            int Ppart = Pidx.Length;

            ConvDiff = new MsrMatrix(Upart, Upart, 1, 1);
            pGrad = new MsrMatrix(Upart, Ppart, 1, 1);
            divVel = new MsrMatrix(Ppart, Upart, 1, 1);
            var VelocityMass = new MsrMatrix(Upart, Upart, 1, 1);          
            var leftChangeBasesVel = new MsrMatrix(Upart, Upart, 1, 1);
            var rightChangeBasesVel = new MsrMatrix(Upart, Upart, 1, 1);

            op.MassMatrix.AccSubMatrixTo(1.0, VelocityMass, Uidx, default(int[]), Uidx, default(int[]));

            op.LeftChangeOfBasis.AccSubMatrixTo(1.0, leftChangeBasesVel, Uidx, default(int[]), Uidx, default(int[]));
            op.RightChangeOfBasis.AccSubMatrixTo(1.0, rightChangeBasesVel, Uidx, default(int[]), Uidx, default(int[]));

            var temp = MsrMatrix.Multiply(leftChangeBasesVel, LocalMatrix);
            LocalMatrix = MsrMatrix.Multiply(temp, rightChangeBasesVel);

            var M = op.OperatorMatrix;

            

            M.AccSubMatrixTo(1.0, ConvDiff, Uidx, default(int[]), Uidx, default(int[]));
            M.AccSubMatrixTo(1.0, pGrad, Uidx, default(int[]), Pidx, default(int[]));
            M.AccSubMatrixTo(1.0, divVel, Pidx, default(int[]), Uidx, default(int[]));

            LocalMatrix.SaveToTextFileSparse("LocalConvDiffMatrix");
            ConvDiff.SaveToTextFileSparse("ConvDiff");

            op.MassMatrix.SaveToTextFileSparse("MassMatrix");

            VelocityMass.SaveToTextFileSparse("VelocityMass");
        }

        public void ResetStat()
        {
            m_Converged = false;
            m_ThisLevelIterations = 0;
        }

        bool m_Converged = false;
        int m_ThisLevelIterations = 0;

        public void Solve<U, V>(U X, V B)
            where U : IList<double>
            where V : IList<double>
        {
            var Bu = new double[Uidx.Length];
            var Xu = Bu.CloneAs();
            Bu = B.GetSubVector(Uidx, default(int[]));
            var Bp = new double[Pidx.Length];
            var Xp = Bp.CloneAs();
            Bp = B.GetSubVector(Pidx, default(int[]));

            Xu = X.GetSubVector(Uidx, default(int[]));
            Xp = X.GetSubVector(Pidx, default(int[]));

            P = new MsrMatrix(Pidx.Length, Pidx.Length);

            MultidimensionalArray Schur = MultidimensionalArray.Create(Pidx.Length,Pidx.Length);
            using (BatchmodeConnector bmc = new BatchmodeConnector())
            {
                bmc.PutSparseMatrix(LocalMatrix, "LocalMatrix");
                bmc.PutSparseMatrix(divVel, "divVel");
                bmc.PutSparseMatrix(pGrad, "pGrad");
                bmc.Cmd("invLocalMatrix = inv(full(LocalMatrix))");
                bmc.Cmd("Poisson = invLocalMatrix*pGrad");
                bmc.Cmd("Schur= divVel*Poisson");
                bmc.GetMatrix(Schur, "Schur");
                bmc.Execute(false);
            }

            P = Schur.ToMsrMatrix();

            P.SaveToTextFileSparse("LocalSchur");


            using (var solver = new ilPSP.LinSolvers.MUMPS.MUMPSSolver())
            {
                solver.DefineMatrix(P);
                solver.Solve(Xp, Bp);
            }

            //var temp2 = Xp.CloneAs();

            //LocalMatrix.SpMV(1, Xp, 0, temp2);

            //using (var solver = new ilPSP.LinSolvers.MUMPS.MUMPSSolver())
            //{
            //    solver.DefineMatrix(divVel);
            //    solver.Solve(Xp, temp2);
            //}           


            // Solve ConvDiff*w=v-q*pGrad
            pGrad.SpMVpara(-1, Xp, 1, Bu);

            using (var solver = new ilPSP.LinSolvers.MUMPS.MUMPSSolver())
            {
                solver.DefineMatrix(ConvDiff);
                solver.Solve(Xu, Bu);
            }

            var temp = new double[Uidx.Length + Pidx.Length];

            for (int i = 0; i < Uidx.Length; i++)
            {
                temp[Uidx[i]] = Xu[i];
            }

            for (int i = 0; i < Pidx.Length; i++)
            {
                temp[Pidx[i]] = Xp[i];
            }

            X.SetV(temp);

        }

    }


    public class LocalDiffusiveFlux : IVolumeForm, IEdgeForm
    {
        public TermActivationFlags BoundaryEdgeTerms {
            get {
                return TermActivationFlags.GradUxV;
            }
        }

        public TermActivationFlags InnerEdgeTerms {
            get {
                return TermActivationFlags.GradUxV;
            }
        }

        public IList<string> ArgumentOrdering {
            get {
                return new string[] { VariableNames.Velocity_d(m_component) };
            }
        }

        public int m_component;

        public IList<string> ParameterOrdering {
            get {
                return VariableNames.Velocity0Vector(2);
            }
        }

        public TermActivationFlags VolTerms {
            get {
                return TermActivationFlags.GradUxGradV | TermActivationFlags.UxV | TermActivationFlags.GradUxV;
            }
        }

        public double BoundaryEdgeForm(ref CommonParamsBnd inp, double[] _uA, double[,] _Grad_uA, double _vA, double[] _Grad_vA)
        {

            int D = inp.D;
            double acc = 0.0;
            for (int d = 0; d < D; d++)
            {
                acc += (_Grad_uA[0, d] * inp.Normale[d] * _vA) * muA;
            }

            return acc;
        }

        public double InnerEdgeForm(ref CommonParams inp, double[] _uIN, double[] _uOUT, double[,] _Grad_uIN, double[,] _Grad_uOUT, double _vIN, double _vOUT, double[] _Grad_vIN, double[] _Grad_vOUT)
        {
            int D = inp.D;
            double acc = 0;
            for (int d = 0; d < D; d++)
            {
                acc += (_Grad_uIN[0, d] * inp.Normale[d] * _vIN - _Grad_uOUT[0, d] * inp.Normale[d] * _vOUT) * muA;
            }

            return -acc;

        }

        public double muA;
        public double dt;


        public double VolumeForm(ref CommonParamsVol cpv, double[] U, double[,] GradU, double V, double[] GradV)
        {
            int D = cpv.D;

            // diffusive
            double acc = 0.0;
            for (int d = 0; d < D; d++)
            {
                acc -= GradU[0, d] * GradV[d] * muA;
            }

            // temporal
            acc -= (1 / dt) * U[0] * V;


            // convective
            //for (int d = 0; d < D; d++)
            //{
            //    acc += cpv.Parameters[d] * GradU[0, d] * V;
            //}


            return -acc;
        }

    }
}
